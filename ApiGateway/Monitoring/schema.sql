BEGIN;
CREATE TABLE IF NOT EXISTS "MonitoringEvents" (
 "Id" uuid PRIMARY KEY DEFAULT gen_random_uuid(), "Kind" text NOT NULL,
 "Service" text NOT NULL, "Level" text NOT NULL, "Action" text NOT NULL,
 "ActorId" uuid NULL, "ResourceId" uuid NULL, "Timestamp" timestamptz NOT NULL DEFAULT now(),
 "Method" text NOT NULL DEFAULT '', "Path" text NOT NULL DEFAULT '',
 "StatusCode" integer NOT NULL, "TraceId" text NOT NULL, "ElapsedMs" double precision NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS "IX_MonitoringEvents_Kind_Time" ON "MonitoringEvents" ("Kind", "Timestamp" DESC, "Id");
CREATE INDEX IF NOT EXISTS "IX_MonitoringEvents_Actor" ON "MonitoringEvents" ("ActorId", "Timestamp" DESC);
CREATE TABLE IF NOT EXISTS "UserNotifications" (
 "Id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
 "UserId" uuid NOT NULL REFERENCES "AppUsers"("Id") ON DELETE CASCADE,
 "EventKey" text NOT NULL, "Kind" text NOT NULL, "Message" text NOT NULL,
 "TargetUrl" text NOT NULL, "DocumentId" uuid NULL REFERENCES "Documents"("Id") ON DELETE CASCADE,
 "Timestamp" timestamptz NOT NULL DEFAULT now(), "ReadAt" timestamptz NULL,
 UNIQUE ("UserId", "EventKey")
);
CREATE INDEX IF NOT EXISTS "IX_UserNotifications_User_Time" ON "UserNotifications" ("UserId", "Timestamp" DESC, "Id");

CREATE OR REPLACE FUNCTION hau_document_activity() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
 doc_status text;
 creator uuid;
 event_kind text;
 event_message text;
 event_action text;
 recipient_role text;
 recipient_department uuid;
BEGIN
 SELECT "Status" INTO doc_status FROM "Documents" WHERE "Id"=NEW."DocId";
 SELECT "FromUserId" INTO creator FROM "DocumentProcesses"
   WHERE "DocId"=NEW."DocId" AND "Action" IN ('Create','Submit') ORDER BY "Timestamp", "Id" LIMIT 1;
 event_action := CASE WHEN NEW."Action"='Submit' AND doc_status='Draft' THEN 'Create' ELSE NEW."Action" END;
 INSERT INTO "MonitoringEvents" ("Kind","Service","Level","Action","ActorId","ResourceId","Timestamp","StatusCode","TraceId")
 VALUES ('Audit','DocumentService','Information',event_action,NEW."FromUserId",NEW."DocId",NEW."Timestamp",200,
   coalesce(nullif(current_setting('hau.trace_id',true),''),'process:'||NEW."Id"::text));

 IF NEW."Action"='Assign' THEN
   event_kind := 'Assignment'; event_message := 'Bạn được phân công xử lý công văn.';
 ELSIF NEW."Action"='SubmitIncoming' THEN
   event_kind := 'ReviewRequested'; event_message := 'Có công văn đến chờ Ban Giám hiệu ký chỉ đạo.'; recipient_role := 'BoardOfDirectors';
 ELSIF NEW."Action"='DirectIncoming' THEN
   event_kind := 'ReviewRequested'; event_message := 'Có công văn đến đã ký chỉ đạo, chờ phân công chuyên viên.'; recipient_role := 'Manager';
   SELECT "ProcessingDepartmentId" INTO recipient_department FROM "Documents" WHERE "Id"=NEW."DocId";
 ELSIF NEW."Action"='Submit' AND doc_status='PendingDeptReview' THEN
   event_kind := 'ReviewRequested'; event_message := 'Có công văn đang chờ ký nháy.'; recipient_role := 'Manager';
 ELSIF NEW."Action"='SubmitDirector' THEN
   event_kind := 'ReviewRequested'; event_message := 'Có công văn đang chờ ký số pháp nhân.'; recipient_role := 'BoardOfDirectors';
 ELSIF NEW."Action"='UpdateOCR' THEN
   event_kind := 'OcrCompleted'; event_message := 'Kết quả OCR công văn đã được cập nhật.';
 ELSIF NEW."Action"='Reject' THEN
   event_kind := 'Rejected'; event_message := 'Công văn của bạn đã bị từ chối.';
 ELSIF NEW."Action" IN ('DeptSign','DirectorSign') THEN
   event_kind := 'Signed'; event_message := 'Công văn của bạn đã hoàn tất một bước ký duyệt.';
 ELSIF NEW."Action"='Publish' THEN
   event_kind := 'Published'; event_message := 'Công văn đã được phát hành.';
 END IF;
 IF event_kind IS NULL THEN RETURN NEW; END IF;

 INSERT INTO "UserNotifications" ("UserId","EventKey","Kind","Message","TargetUrl","DocumentId","Timestamp")
 SELECT DISTINCT u."Id",'process:'||NEW."Id"::text,event_kind,event_message,
   '/documents/'||NEW."DocId"::text,NEW."DocId",NEW."Timestamp"
 FROM "AppUsers" u WHERE u."IsActive" AND (
   (NEW."Action"='Assign' AND u."Id"=NEW."ToUserId") OR
   (recipient_role IS NOT NULL AND (recipient_department IS NULL OR u."DepartmentId"=recipient_department) AND EXISTS (
     SELECT 1 FROM "AppUserRoles" ur JOIN "AppRoles" r ON r."Id"=ur."RoleId"
     WHERE ur."UserId"=u."Id" AND r."RoleName"=recipient_role)) OR
   (recipient_role IS NULL AND NEW."Action"<>'Assign' AND u."Id"=creator) OR
   (NEW."Action"='Publish' AND EXISTS (
     SELECT 1 FROM "DocumentProcesses" p WHERE p."DocId"=NEW."DocId" AND p."Action"='Assign' AND p."ToUserId"=u."Id"))
 ) ON CONFLICT ("UserId","EventKey") DO NOTHING;
 RETURN NEW;
END;
$$;
DROP TRIGGER IF EXISTS hau_document_activity_trigger ON "DocumentProcesses";
CREATE TRIGGER hau_document_activity_trigger AFTER INSERT ON "DocumentProcesses"
 FOR EACH ROW EXECUTE FUNCTION hau_document_activity();
-- PostgreSQL publishes only after commit, including writes made by other services.
CREATE OR REPLACE FUNCTION hau_notification_changed() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 PERFORM pg_notify('hau_notifications', CASE WHEN TG_OP='DELETE' THEN OLD."UserId" ELSE NEW."UserId" END::text);
 RETURN NULL;
END;
$$;
DROP TRIGGER IF EXISTS hau_notification_changed_trigger ON "UserNotifications";
CREATE TRIGGER hau_notification_changed_trigger AFTER INSERT OR UPDATE OR DELETE ON "UserNotifications"
 FOR EACH ROW EXECUTE FUNCTION hau_notification_changed();

CREATE OR REPLACE FUNCTION hau_notification_visibility_changed() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE recipient uuid;
BEGIN
 IF NEW."IsDeleted" IS DISTINCT FROM OLD."IsDeleted" THEN
   FOR recipient IN SELECT DISTINCT "UserId" FROM "UserNotifications" WHERE "DocumentId"=NEW."Id" LOOP
     PERFORM pg_notify('hau_notifications', recipient::text);
   END LOOP;
 END IF;
 RETURN NULL;
END;
$$;
DROP TRIGGER IF EXISTS hau_notification_visibility_trigger ON "Documents";
CREATE TRIGGER hau_notification_visibility_trigger AFTER UPDATE OF "IsDeleted" ON "Documents"
 FOR EACH ROW EXECUTE FUNCTION hau_notification_visibility_changed();
-- Invalidate only clients watching this document. No OCR contents or user data travel in NOTIFY.
CREATE OR REPLACE FUNCTION hau_document_result_changed() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF TG_OP='DELETE' THEN
   PERFORM pg_notify('hau_document_updates', OLD."Id"::text);
 ELSIF NEW."OcrDataRaw" IS DISTINCT FROM OLD."OcrDataRaw"
       OR NEW."IsDeleted" IS DISTINCT FROM OLD."IsDeleted" THEN
   PERFORM pg_notify('hau_document_updates', NEW."Id"::text);
 END IF;
 RETURN NULL;
END;
$$;
DROP TRIGGER IF EXISTS hau_document_result_trigger ON "Documents";
CREATE TRIGGER hau_document_result_trigger AFTER UPDATE OF "OcrDataRaw","IsDeleted" OR DELETE ON "Documents"
 FOR EACH ROW EXECUTE FUNCTION hau_document_result_changed();
COMMIT;
