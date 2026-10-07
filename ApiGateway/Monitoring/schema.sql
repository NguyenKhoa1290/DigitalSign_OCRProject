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
BEGIN
 SELECT "Status" INTO doc_status FROM "Documents" WHERE "Id"=NEW."DocId";
 SELECT "FromUserId" INTO creator FROM "DocumentProcesses"
   WHERE "DocId"=NEW."DocId" AND "Action"='Submit' ORDER BY "Timestamp", "Id" LIMIT 1;
 event_action := CASE WHEN NEW."Action"='Submit' AND doc_status='Draft' THEN 'Create' ELSE NEW."Action" END;
 INSERT INTO "MonitoringEvents" ("Kind","Service","Level","Action","ActorId","ResourceId","Timestamp","StatusCode","TraceId")
 VALUES ('Audit','DocumentService','Information',event_action,NEW."FromUserId",NEW."DocId",NEW."Timestamp",200,
   coalesce(nullif(current_setting('hau.trace_id',true),''),'process:'||NEW."Id"::text));

 IF NEW."Action"='Assign' THEN
   event_kind := 'Assignment'; event_message := 'Bạn được phân công xử lý công văn.';
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
   (recipient_role IS NOT NULL AND EXISTS (
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
COMMIT;
