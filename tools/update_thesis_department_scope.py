"""Update the approved thesis using package-preserving OOXML edits.

Run in the isolated report-tools container. Preserve the approved source and
unrelated styles, headers, footers, media and section setup.
"""
import copy
import hashlib
import importlib.util
import json
import io
from pathlib import Path
from zipfile import ZipFile
from lxml import etree as E
from docx import Document
from PIL import Image, ImageDraw, ImageFont

BASE = Path('/work')
SOURCE = BASE / 'tai_lieu/05_bao_cao/Do_an_tot_nghiep_cap_nhat_luong_va_phan_quyen_20261025.docx'
OUTPUT = BASE / 'tai_lieu/05_bao_cao/Do_an_tot_nghiep_cap_nhat_phan_quyen_don_vi_20261105.docx'
PROOF = BASE / 'tai_nguyen/kiem_tra_bao_cao/phan_quyen_don_vi_20261105'
NS = {'w': 'http://schemas.openxmlformats.org/wordprocessingml/2006/main'}
W = '{' + NS['w'] + '}'

def text(node):
    return ''.join(node.xpath('.//w:t/text()', namespaces=NS))

def replace_paragraph(p, value):
    runs = p.findall('w:r', NS)
    template = next((r for r in runs if r.find('w:t', NS) is not None), None)
    normal = next((r for r in runs if r.find('w:t',NS) is not None and r.find('w:rPr/w:b',NS) is None),None)
    for child in list(p):
        if child.tag != W + 'pPr': p.remove(child)
    pieces=[(template,value)]
    if template is not None and template.find('w:rPr/w:b',NS) is not None and normal is not None and ':' in value:
        label,content=value.split(':',1);pieces=[(template,label+':'),(normal,content)]
    for style,content in pieces:
        run=E.SubElement(p,W+'r')
        if style is not None and style.find('w:rPr',NS) is not None:run.append(copy.deepcopy(style.find('w:rPr',NS)))
        t=E.SubElement(run,W+'t');t.text=content;t.set('{http://www.w3.org/XML/1998/namespace}space','preserve')

def set_cell(cell, values):
    if isinstance(values, str): values = [values]
    paragraphs = cell.findall('w:p', NS)
    template = next((p for p in paragraphs if text(p)), paragraphs[0])
    for p in paragraphs: cell.remove(p)
    for value in values:
        p = copy.deepcopy(template); replace_paragraph(p, value); cell.append(p)

def scope_erd():
    """Logical relationships and selected fields; preserve the source aspect ratio."""
    im=Image.new('RGB',(1272,1206),'white');d=ImageDraw.Draw(im)
    regular=ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf',20)
    bold=ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf',23)
    def line(points,label=None,pos=None):
        d.line(points,fill='#586579',width=2)
        if label:d.text(pos,label,font=regular,fill='#24364d',stroke_width=3,stroke_fill='white')
    line([(605,164),(605,245)],'1 : n',(612,185))
    line([(430,75),(400,75),(400,150),(430,150)],'cha',(350,100))
    line([(780,90),(850,90),(850,350),(890,350)],'đơn vị',(782,190))
    line([(1095,135),(1095,250)],'1 : n',(1102,193))
    line([(280,420),(330,420),(330,955),(280,955)],'1 : n',(285,580))
    line([(450,455),(370,455),(370,955),(330,955)],'1 : n',(381,570))
    line([(605,529),(605,855)],'người ký',(615,720))
    line([(780,485),(890,485)],'người tạo',(785,452))
    line([(1030,766),(1030,850)],'1 : n',(1040,775))
    line([(970,766),(970,800),(745,800),(745,855)],'hồ sơ được ký',(765,775))
    def box(x,y,w,title,fields):
        h=52+len(fields)*29
        d.rounded_rectangle((x,y,x+w,y+h),radius=6,fill='white',outline='#73a9df',width=2)
        d.text((x+w/2,y+24),title,font=bold,fill='#17273a',anchor='mm')
        d.line((x,y+49,x+w,y+49),fill='#73a9df',width=2)
        for i,s in enumerate(fields):d.text((x+12,y+57+i*29),s,font=regular,fill='#182536')
    box(430,25,350,'Departments',['id : UUID (PK)','dept_name : varchar','parent_id : UUID'])
    box(920,25,330,'DocumentTypes',['id : UUID (PK)','type_name : varchar'])
    box(20,300,260,'AppRoles',['id : UUID (PK)','role_name : varchar','description : varchar'])
    box(430,245,350,'AppUsers',['id : UUID (PK)','username : varchar','password_hash : text','full_name : varchar','email : varchar','department_id : UUID','is_active : boolean','...'])
    box(890,250,360,'Documents',['id : UUID (PK)','doc_number : varchar','title : varchar','minio_path : text','status : varchar','doc_type_id : UUID','created_by_id : UUID','created_at : datetime','department_id : UUID','department_name : varchar','is_school_wide : boolean','processing_department_id','concurrency_version : UUID','is_deleted : boolean','ocr_data_raw : jsonb','...'])
    box(20,900,310,'AppUserRoles',['user_id : UUID','role_id : UUID'])
    box(430,855,350,'Signatures',['id : UUID (PK)','doc_id : UUID','signer_id : UUID','signature_type : varchar','signed_at : datetime'])
    box(890,850,360,'DocumentProcesses',['id : UUID (PK)','doc_id : UUID','from_user_id : UUID','to_user_id : UUID','action : varchar','timestamp : datetime'])
    d.text((636,1168),'Quan hệ logic và các trường chính; chi tiết tại mục 2.6.3',font=regular,fill='#42536a',anchor='mm')
    output=io.BytesIO();im.save(output,format='PNG');return output.getvalue()

def main():
    if OUTPUT.exists(): raise SystemExit('Output already exists; preserve it before rerunning.')
    PROOF.mkdir(parents=True, exist_ok=True)
    with ZipFile(SOURCE) as zin:
        original = zin.read('word/document.xml')
        root = E.fromstring(original); before = copy.deepcopy(root)
        body = root.find('w:body', NS); blocks = list(body); changes = []
        def change(index, prefix, value):
            p = blocks[index]; old = text(p)
            assert p.tag == W+'p' and old.startswith(prefix), (index, old[:90])
            replace_paragraph(p, value)
            changes.append({'block': index, 'before': old, 'after': value})
        def add_after(index, value):
            template = blocks[index]
            assert template.tag == W+'p'
            p = copy.deepcopy(template); replace_paragraph(p,value); template.addnext(p)
            changes.append({'insert_after':index,'after':value})
        def cell(block, row, col, values):
            table = blocks[block]; cells = table.findall('w:tr',NS)[row].findall('w:tc',NS)
            old = text(cells[col]); set_cell(cells[col],values)
            changes.append({'table_block':block,'row':row,'column':col,'before':old,'after':values})

        change(158, 'Cơ chế:', 'Cơ chế: Tải tệp chỉ lưu nguồn. Khi người có quyền chọn model và tối đa 5 trang rồi gửi yêu cầu OCR, Document Service lưu tác vụ, trả HTTP 202 và dùng outbox phát JobId qua Kafka. Worker xử lý nền, lưu kết quả và thông báo hoàn tất hoặc lỗi.')
        change(191, 'Cấu hình luồng quy trình', 'Quản trị quy trình: Admin quản lý đơn vị, tài khoản và cấu hình dịch vụ; có toàn quyền hỗ trợ nghiệp vụ công văn. Các bước luân chuyển vẫn được kiểm tra theo giai đoạn và điều kiện ký, không tự đổi trạng thái chỉ vì có quyền quản trị.')
        change(192, 'Phân quyền truy cập', 'Phân quyền truy cập: Quyền thao tác được xác định bằng vai trò kết hợp đơn vị công tác, quan hệ với hồ sơ và giai đoạn xử lý. Admin gán đơn vị cho tài khoản; hệ thống lấy đơn vị người tạo để xác định phạm vi công văn, không nhận đơn vị tự khai từ phía người dùng.')
        change(194, 'Việc xác định rõ vai trò Admin', 'Admin được xem toàn trường và thực hiện các bước nghiệp vụ được cấp, kể cả hỗ trợ ký số bằng chứng thư gắn với chính tài khoản. Mọi thao tác phải ghi danh tính thực hiện, tuân theo điều kiện của bước xử lý và lưu lịch sử.')
        change(196, 'Nhóm tác nhân Văn thư', 'Văn thư tiếp nhận, số hóa, phát hành và lưu trữ hồ sơ trong đơn vị mình cùng công văn chung. Vai trò này thường xuyên sử dụng OCR nhưng không có quyền xem toàn trường chỉ từ chức danh Văn thư.')
        change(198, 'Tiếp nhận và Số hóa:', 'Tiếp nhận và Số hóa: Văn thư tạo công văn đến, tải ảnh/PDF/Word và hoàn thiện thông tin trước khi trình Ban Giám hiệu chỉ đạo. Hệ thống lưu đơn vị người tạo tại thời điểm tạo. Văn thư không phân công Chuyên viên xử lý.')
        change(199, 'Vận hành OCR:', 'Vận hành OCR: Văn thư chọn model Gemini và tối đa 5 trang ảnh/PDF mỗi lượt. Có thể OCR và tải Word hiệu chỉnh cho cả công văn đến và đi thuộc phạm vi khi còn Nháp hoặc Trả sửa. Word hoàn chỉnh không OCR; hỗ trợ OCR không cấp quyền sửa dự thảo chiều đi thay Chuyên viên.')
        change(200, 'Phát hành và Lưu trữ:', 'Phát hành và Lưu trữ: Văn thư thuộc phạm vi được phép kiểm tra công văn đi đã hoàn tất ký cấp phòng và ký pháp nhân, sau đó phát hành, lưu trữ. Văn thư không tạo hoặc nộp trình duyệt chiều đi, không thay tệp dự thảo thay Chuyên viên.')
        change(204, 'Soạn thảo:', 'Soạn thảo: Chuyên viên tạo và trình công văn đi của mình; đơn vị công tác được ghi vào hồ sơ tại lúc tạo. Người thuộc đơn vị cấp Trường tạo công văn chung. Chuyên viên sửa dự thảo khi bị trả lại, không được chạy OCR hoặc tải Word vào phần hiệu chỉnh OCR.')
        change(208, 'Lãnh đạo Phòng', 'Lãnh đạo Phòng kiểm soát chuyên môn trong đơn vị mình. Công văn đi của đơn vị khác không thuộc thẩm quyền ký nháy; công văn chung do Lãnh đạo Phòng thuộc đơn vị cấp Trường thực hiện. Quyền xem hồ sơ không đồng nghĩa quyền ký.')
        change(211, 'Ký số cấp đơn vị:', 'Ký số cấp đơn vị: Lãnh đạo Phòng ký nháy PDF công văn đi đúng đơn vị ở bước chờ duyệt phòng, rồi trình Ban Giám hiệu. Mục Ký số có danh sách Chờ ký theo thẩm quyền và Đã ký theo tài khoản thực hiện.')
        change(212, 'Phân công nhiệm vụ:', 'Phân công nhiệm vụ: Với công văn đến đã được Ban Giám hiệu ký chỉ đạo, Lãnh đạo Phòng của đúng đơn vị xử lý chọn Chuyên viên thuộc đơn vị hoặc đơn vị trực thuộc và ký phân công. Công văn đi không có bước phân công xử lý.')
        change(225, 'Quản trị viên (Admin):', 'Quản trị viên (Admin): Có quyền xem dữ liệu toàn trường, quản lý tài khoản và đơn vị, giám sát hệ thống và hỗ trợ các bước nghiệp vụ công văn. Khi ký, hệ thống ghi nhận chính danh tính và chứng thư của Admin; không giả danh lãnh đạo khác.')
        change(226, 'Văn thư:', 'Văn thư: Tạo/tiếp nhận công văn đến; OCR và tải Word hiệu chỉnh của ảnh/PDF còn Nháp hoặc Trả sửa trong cả hai chiều; phát hành công văn đi sau ký. Phạm vi dữ liệu là đơn vị mình, công văn chung và hồ sơ trực tiếp liên quan theo quyền; không phân công xử lý.')
        change(228, 'Lãnh đạo Phòng:', 'Lãnh đạo Phòng: Duyệt, ký nháy và trình công văn đi của đơn vị; ký phân công công văn đến theo đơn vị được Ban Giám hiệu chỉ đạo. Công văn chung yêu cầu lãnh đạo thuộc đơn vị cấp Trường. Danh sách đã ký lấy từ chữ ký của chính tài khoản.')
        change(235, 'Tra cứu Metadata:', 'Tra cứu Metadata: Người dùng tìm theo tiêu đề, số hiệu, loại và phạm vi được phép. Hồ sơ đơn vị, công văn chung và các quan hệ người tạo/người được giao/người đã ký được kiểm tra tại API trước khi phân trang; Ban Giám hiệu và Admin xem toàn trường.')
        change(240, 'Vận hành OCR:', 'Vận hành OCR: Văn thư xử lý ảnh/PDF ở Nháp hoặc Trả sửa thuộc phạm vi cả hai chiều bằng PaddleOCR kết hợp Gemini, theo dõi tác vụ nền và hiệu chỉnh Word bên ngoài. Chuyên viên chỉ xem kết quả/lịch sử theo quyền, không chạy hoặc tải Word hiệu chỉnh OCR.')
        change(242, 'Xem báo cáo số lượng:', 'Xem báo cáo số lượng: Chỉ số công văn và thông báo của Văn thư áp dụng cùng phạm vi đơn vị và công văn chung; không tổng hợp hồ sơ riêng của đơn vị khác.')
        change(249, 'Phân công nhiệm vụ:', 'Phân công nhiệm vụ: Chỉ xuất hiện ở chiều đến chờ phân công và đúng đơn vị Ban Giám hiệu đã chỉ đạo; chọn Chuyên viên hợp lệ và ký số. Không sử dụng chức năng này cho công văn đi.')
        change(250, 'Ký số nội bộ:', 'Ký số nội bộ: Chọn công văn trong Chờ ký đúng đơn vị, kiểm tra PDF và ký nháy trước khi trình Ban Giám hiệu. Đã ký hiển thị hồ sơ có chữ ký của tài khoản, thời điểm ký và liên kết xem; không suy ra từ trạng thái chung của hồ sơ.')
        change(271, 'Văn thư đối chiếu', 'Văn thư đối chiếu nội dung và thông tin được phép với bản gốc, tải Word để sửa bằng trình soạn thảo ngoài rồi tải DOCX hiệu chỉnh vào phần kết quả OCR. Bản sửa ghi tên người sửa, thời điểm và lần sửa; giữ tệp nguồn. Chuyên viên được xem lịch sử, không được tải Word hiệu chỉnh OCR.')
        change(272, 'Phát hành và Lưu trữ:', 'Phát hành và Lưu trữ: Công văn đi thuộc phạm vi đã có chữ ký nháy và chữ ký pháp nhân được Văn thư phát hành, lưu trữ. Phát hành là bước riêng sau ký; Văn thư không ký pháp nhân thay Ban Giám hiệu.')
        change(290, 'Phân công nhiệm vụ:', 'Phân công nhiệm vụ: Sau chữ ký chỉ đạo của Ban Giám hiệu, chỉ Lãnh đạo Phòng đúng đơn vị xử lý mới chọn Chuyên viên hợp lệ và ký phân công. Hồ sơ chiều đi không có thao tác phân công.')
        change(292, 'Ký số nội bộ/Ký nháy:', 'Ký số nội bộ: Chỉ ký nháy PDF công văn đi của đúng đơn vị trong PendingDeptReview; công văn chung yêu cầu đơn vị cấp Trường. API kiểm tra lại thẩm quyền và người ký thực tế trước khi truy cập tệp hoặc sử dụng chứng thư.')
        change(303, 'Ghi bút phê chỉ đạo:', 'Ký chỉ đạo: Ban Giám hiệu chọn đơn vị xử lý, nhập ý kiến và ký PDF công văn đến. Hồ sơ chuyển chờ ký phân công khi thành công; ý kiến được lưu trong lịch sử. Chưa có chức năng bố trí vùng bút phê hiển thị trực tiếp trên PDF.')
        change(316, 'Cấu hình luồng quy trình:', 'Quản trị luồng xử lý: Admin quản lý đơn vị và quyền tài khoản, hỗ trợ các thao tác nghiệp vụ và giám sát lịch sử. Các luồng đến/đi được kiểm tra theo quy tắc đã xây dựng; chưa có trình thiết kế workflow tùy ý trên giao diện.')

        # Existing use-case tables keep their grid, cell styles and repeated headers.
        cell(323,2,1,'Văn thư; Admin hỗ trợ theo quyền.')
        cell(323,3,1,'Văn thư đã đăng nhập, đang hoạt động và thuộc đơn vị hợp lệ. Hồ sơ thuộc phạm vi đơn vị hoặc công văn chung; OCR chỉ thực hiện với ảnh/PDF ở Nháp hoặc Trả sửa.')
        cell(323,5,1,[
            '1. Văn thư tạo công văn đến; hệ thống lấy đơn vị tài khoản, ghi vào hồ sơ. Người thuộc cấp Trường tạo công văn chung.',
            '2. Tải ảnh/PDF và xem trước; Word hoàn chỉnh được lưu như văn bản, không OCR.',
            '3. Chọn model và tối đa 5 trang, gửi lệnh OCR; API kiểm tra phạm vi, giai đoạn và phiên bản rồi trả HTTP 202.',
            '4. Kafka phân phối JobId; worker dùng PaddleOCR và Gemini, lưu kết quả/lịch sử và thông báo xong hoặc lỗi.',
            '5. Văn thư tải Word, sửa bằng trình soạn thảo ngoài và tải bản hiệu chỉnh; hệ thống ghi người sửa, thời gian, lần sửa và giữ nguồn.',
            '6. Chuẩn bị PDF phù hợp, trình Ban Giám hiệu ký chỉ đạo. Hỗ trợ OCR chiều đi ở Nháp/Trả sửa không thay quyền tác giả của Chuyên viên.'
        ])
        cell(323,6,1,[
            'Sai phạm vi, thiếu tệp hoặc sai giai đoạn: từ chối trước khi gọi OCR.',
            'Quá 5 trang mỗi lượt hoặc model hết hạn mức: yêu cầu chọn lại; lỗi tác vụ giữ kết quả cũ.',
            'Nguồn thay đổi hoặc hồ sơ bị xóa: không gắn kết quả cũ. Word hiệu chỉnh không tự thay PDF nguồn hoặc chữ ký.'
        ])
        cell(325,3,1,'Chuyên viên đăng nhập, đang hoạt động và có đơn vị. Admin có thể hỗ trợ. Công văn đi được gắn đơn vị người tạo, hoặc đánh dấu chung nếu thuộc đơn vị cấp Trường.')
        cell(325,5,1,[
            '1. Chuyên viên tạo công văn đi và nhập thông tin; server xác định đơn vị từ tài khoản, không nhận đơn vị giả từ client.',
            '2. Tải bản dự thảo; Word là văn bản hoàn chỉnh, bước ký cần PDF. Chuyên viên không chạy OCR.',
            '3. Nếu nguồn ảnh/PDF cần số hóa, Văn thư cùng phạm vi có thể hỗ trợ OCR và hiệu chỉnh khi Nháp/Trả sửa.',
            '4. Người tạo kiểm tra và nộp trình duyệt; hệ thống ghi lịch sử, thông báo Lãnh đạo Phòng đúng đơn vị.',
            '5. Lãnh đạo yêu cầu sửa thì trả lại Chuyên viên đã soạn; không giao Văn thư thay tệp dự thảo.',
            '6. Đơn vị hồ sơ giữ nguyên khi người tạo chuyển đơn vị; công văn chung do Lãnh đạo Phòng cấp Trường ký nháy.'
        ])
        cell(328,2,1,'Lãnh đạo Phòng đúng đơn vị; Admin hỗ trợ bằng danh tính của mình.')
        cell(328,3,1,'Chiều đi ở PendingDeptReview và cùng đơn vị hồ sơ; công văn chung thuộc cấp Trường. Chiều đến ở PendingAssignment và đúng đơn vị Ban Giám hiệu chỉ đạo. Có PDF/chứng thư phù hợp.')
        cell(328,5,1,[
            '1. Mở Chờ ký theo vai trò, đơn vị và giai đoạn; xem hồ sơ/PDF trong phạm vi được phép.',
            '2. Chiều đến: kiểm tra chữ ký chỉ đạo, chọn Chuyên viên đơn vị hoặc trực thuộc, nhập ý kiến và ký phân công.',
            '3. Khi ký chiều đến thành công, chuyển IncomingAssigned, lưu lịch sử và báo người nhận.',
            '4. Chiều đi: duyệt nội dung và ký nháy PDF bằng chứng thư của tài khoản. SignService không tin signerId tự khai.',
            '5. Ghi chữ ký rồi cập nhật DeptSigned; nếu cập nhật lỗi, thử Hoàn tất lại mà không ký lặp. Sau đó trình BGH.',
            '6. Đã ký liệt kê chữ ký của chính tài khoản. Nếu cần sửa, trả về Chuyên viên tác giả; không phân công chiều đi.'
        ])
        cell(328,6,1,[
            'Khác đơn vị hoặc chỉ có quyền xem công văn chung: không được ký nháy; Admin là ngoại lệ quyền quản trị.',
            'Thiếu chữ ký BGH hợp lệ ở chiều đến, sai người nhận hoặc chứng thư lỗi: không phân công.',
            'Ký PDF xong nhưng cập nhật trạng thái lỗi: giữ bằng chứng ký, cho hoàn tất lại; không thông báo đã phát hành.',
            'Thời hạn, báo cáo kết quả và duyệt hoàn thành vẫn là phần cần phát triển.'
        ])
        cell(331,2,1,'Ban Giám hiệu; Admin hỗ trợ bằng chứng thư của chính tài khoản.')
        cell(331,3,1,'Tài khoản đang hoạt động, có PDF/chứng thư phù hợp. Chiều đến ở PendingDirection; chiều đi đã ký nháy và được trình vào PendingDirectorSign.')
        cell(331,5,1,[
            '1. Ban Giám hiệu xem hồ sơ và kiểm tra PDF, lịch sử cùng giai đoạn xử lý.',
            '2. Chiều đến: chọn đơn vị xử lý, nhập ý kiến và ký IncomingDirection; thành công mới chuyển PendingAssignment.',
            '3. Lãnh đạo Phòng đúng đơn vị tiếp tục ký phân công. Admin có thể hỗ trợ các bước theo quyền.',
            '4. Chiều đi: kiểm tra chữ ký nháy và ký LegalSeal bằng chứng thư của chính tài khoản.',
            '5. Lưu PDF/chữ ký rồi cập nhật DirectorSigned; lỗi cập nhật có thể Hoàn tất lại mà không ký lặp.',
            '6. Văn thư trong phạm vi được phép phát hành riêng sau đủ chữ ký; hệ thống mới chuyển Published.'
        ])
        cell(331,6,1,[
            'Sai giai đoạn, thiếu chữ ký trước đó hoặc chứng thư lỗi: từ chối và giữ trạng thái cũ.',
            'Ký xong nhưng cập nhật trạng thái lỗi: giữ chữ ký, cho thử hoàn tất lại; chưa coi là đã phát hành.',
            'Cần sửa nội dung thì trả về Chuyên viên tác giả. Admin hỗ trợ bằng danh tính của mình, không giả danh BGH.'
        ])
        cell(334,4,1,'Tài khoản, đơn vị, vai trò hoặc cấu hình dịch vụ được lưu; thao tác quản trị có lịch sử. Các bước đến/đi vẫn tuân theo quy tắc nghiệp vụ.')
        cell(334,5,1,[
            '1. Admin mở quản lý tổ chức và chọn thêm hoặc sửa tài khoản.',
            '2. Nhập mã cán bộ/giảng viên, thông tin cá nhân, mật khẩu; chọn đơn vị và vai trò phù hợp.',
            '3. Lưu thay đổi. Identity Service kiểm tra dữ liệu và cập nhật tài khoản trong PostgreSQL.',
            '4. Admin cấu hình dịch vụ OCR hoặc xem nhật ký tại Hoạt động ứng dụng theo chức năng hiện có.',
            '5. Các bước luân chuyển được kiểm tra theo quy tắc đã xây dựng; chưa có trình thiết kế workflow tùy ý trên giao diện.',
            '6. Hệ thống thông báo kết quả; người dùng đăng nhập để nhận quyền và phạm vi tương ứng.'
        ])
        cell(337,6,1,[
            'Không có quyền hoặc không phải người tạo: từ chối, không thay đổi hồ sơ.',
            'Hồ sơ đã trình, trả sửa, phân công, ký, tiếp nhận chính thức hoặc phát hành: không dùng chức năng xóa mềm dự thảo, kể cả Admin. Admin hủy bằng thao tác riêng có lý do.',
            'Hồ sơ thay đổi đồng thời, thiếu lý do hoặc thiếu bằng chứng người tạo/giai đoạn: từ chối và yêu cầu kiểm tra lại.',
            'Giao dịch lỗi: giữ nguyên hồ sơ; OCR và callback không được cập nhật hoặc làm sống lại hồ sơ đã xóa.'
        ])
        cell(349,1,1,'Xem toàn trường, quản lý tài khoản/đơn vị và hỗ trợ các bước nghiệp vụ; ghi danh tính thực hiện, không bỏ điều kiện ký.')
        cell(349,2,1,'Tiếp nhận chiều đến; OCR/hiệu chỉnh ảnh/PDF Nháp/Trả sửa cả hai chiều; phát hành/lưu trữ trong phạm vi đơn vị và công văn chung.')
        cell(349,3,1,'Soạn, sửa và trình công văn đi của mình; xử lý chiều đến được giao; xem kết quả/lịch sử theo quyền, không chạy hoặc tải Word hiệu chỉnh OCR.')
        cell(349,4,1,'Ký nháy chiều đi đúng đơn vị; ký phân công chiều đến theo đơn vị được chỉ đạo; xem Chờ ký và lịch sử chữ ký của mình.')
        cell(349,5,1,'Xem toàn trường, ký chỉ đạo chiều đến và ký pháp nhân chiều đi đúng giai đoạn; lịch sử đã ký theo tài khoản.')

        # Keep use-case headings/headers with their first content, and avoid
        # single-line fragments at the start of a continued table page.
        for index in [321,323,325,328,331,334,337]:
            table=blocks[index]
            previous=table.getprevious()
            if previous is not None and previous.tag==W+'p':
                ppr=previous.find('w:pPr',NS)
                if ppr is None:ppr=E.SubElement(previous,W+'pPr')
                E.SubElement(ppr,W+'keepNext')
            for row_index,row in enumerate(table.findall('w:tr',NS)):
                for p in row.findall('.//w:p',NS):
                    ppr=p.find('w:pPr',NS)
                    if ppr is None:ppr=E.SubElement(p,W+'pPr')
                    for name in ['keepLines','widowControl']:
                        if ppr.find('w:'+name,NS) is None:E.SubElement(ppr,W+name)
                    if row_index==0 and ppr.find('w:keepNext',NS) is None:E.SubElement(ppr,W+'keepNext')
        for row in blocks[349].findall('w:tr',NS):
            pr=row.find('w:trPr',NS)
            if pr is None:pr=E.SubElement(row,W+'trPr')
            if pr.find('w:cantSplit',NS) is None:E.SubElement(pr,W+'cantSplit')

        matrix=blocks[355]; rows=matrix.findall('w:tr',NS)
        for row in [7,17,18,19,20]:cell(355,row,1,'X (4)')
        cell(355,14,0,'Chạy OCR ảnh/PDF được phép')
        cell(355,15,0,'Tải Word hiệu chỉnh OCR')
        def matrix_row(after, values):
            row=copy.deepcopy(rows[16])
            for c,v in zip(row.findall('w:tc',NS),values):set_cell(c,v)
            after.addnext(row);return row
        row=matrix_row(rows[10],['Admin hủy hồ sơ có lý do','X','','','',''])
        matrix_row(row,['Xóa vĩnh viễn hồ sơ đã xóa','X','','','',''])
        row=matrix_row(rows[16],['Xem công văn theo phạm vi (5)','X','X','X','X','X'])
        matrix_row(row,['Xem danh sách chính mình đã ký','X','','','X','X'])
        change(357, 'Ma trận được cập nhật', 'Ma trận cập nhật ngày 05/11/2026 kết hợp vai trò, đơn vị và giai đoạn. Phân biệt tạo/trình công văn đi, tiếp nhận chiều đến, hỗ trợ OCR cả hai chiều và quyền ký. Dấu X chỉ có hiệu lực khi thỏa điều kiện; xem kết quả không cấp quyền chạy OCR.')
        change(359, '(2) Admin', '(2) Admin hỗ trợ xóa mềm dự thảo đủ điều kiện và nhập lý do. Hủy hồ sơ là quyền quản trị riêng, có lý do và lịch sử; không dùng thao tác xóa mềm dự thảo để bỏ qua giai đoạn nghiệp vụ.')
        change(361, '(4) Admin', '(4) Admin có toàn quyền nghiệp vụ và xem toàn trường, nhưng khi ký phải dùng danh tính/chứng thư của mình. Văn thư chỉ OCR/tải Word hiệu chỉnh ảnh/PDF còn Nháp hoặc Trả sửa trong phạm vi cả hai chiều, không thay tệp dự thảo chiều đi. Chuyên viên chỉ trình/sửa công văn đi của mình.')
        add_after(361,'(5) Văn thư, Chuyên viên và Lãnh đạo Phòng xem công văn đơn vị mình, công văn chung hoặc hồ sơ do mình tạo, được giao hay đã ký. Ban Giám hiệu/Admin xem toàn trường. Công văn chung do Lãnh đạo Phòng đơn vị cấp Trường ký nháy; đơn vị khác chỉ xem, không được ký nhờ quyền xem.')
        change(362, 'Không cho xóa công văn', 'Xóa mềm dự thảo không áp dụng cho hồ sơ đã trình, trả sửa, phân công, ký, phát hành hoặc tiếp nhận chính thức. Admin có thao tác hủy riêng và xóa vĩnh viễn hồ sơ đã xóa theo xác nhận, lý do và kiểm soát đồng thời. Khôi phục chỉ áp dụng khi còn đủ điều kiện; không khôi phục tùy ý hồ sơ đã đi qua quy trình.')
        change(382, 'Hiển thị kết quả:', 'Hiển thị kết quả: Tab Công văn hiển thị đơn vị, nhãn Công văn chung và bộ lọc phạm vi. Tab Ký số tách Chờ ký theo thẩm quyền và Đã ký theo tài khoản, kèm thời điểm và liên kết hồ sơ. Giao diện hỗ trợ desktop/mobile; API kiểm tra lại quyền khi thực hiện thao tác.')
        change(387, 'Xác thực tập trung:', 'Xác thực và kiểm soát truy cập: Gateway kiểm tra JWT, còn dịch vụ nghiệp vụ xác minh lại quyền theo hồ sơ. Danh sách thông báo, số chưa đọc và SSE theo công văn dùng cùng phạm vi; thông báo chờ ký nháy chỉ gửi Lãnh đạo Phòng của đơn vị hồ sơ.')
        change(396, 'Dịch vụ chịu trách nhiệm', 'Phân quyền kết hợp: Identity Service quản lý vai trò và đơn vị của tài khoản. Các dịch vụ dùng JWT xác định người gọi, tra cứu dữ liệu khi cần và kết hợp vai trò với đơn vị, quan hệ hồ sơ, giai đoạn; không chỉ dựa vào thông tin quyền do client gửi.')
        change(405, 'Tiếp nhận và Định danh văn bản:', 'Tiếp nhận và Định danh văn bản: Khi tạo hồ sơ, dịch vụ ghi người tạo, thời gian và đơn vị lấy từ tài khoản đã xác thực. Người thuộc đơn vị gốc cấp Trường tạo công văn chung. Phạm vi này lưu cố định trên hồ sơ, không tự đổi khi người tạo chuyển công tác.')
        change(409, 'Điều phối quy trình bất đồng bộ', 'Điều phối OCR qua Kafka: Tải tệp chỉ lưu nguồn, không tự chạy OCR. Khi người có quyền chọn model/trang và gửi lệnh, dịch vụ lưu tác vụ rồi trả HTTP 202; outbox đưa JobId lên Kafka. Worker kiểm tra lại quyền, giai đoạn và phiên bản nguồn trước khi gọi OCR Service.')
        change(410, 'Kiểm soát trạng thái', 'Kiểm soát trạng thái và phạm vi: Danh sách, tìm kiếm, thống kê, chi tiết, tệp và kết quả OCR áp dụng quyền ở server trước khi phân trang. Hồ sơ đơn vị khác bị chặn khi truy cập trực tiếp. Quyền xem hồ sơ chung không tự cấp quyền sửa hoặc ký; hồ sơ đã xóa bị ẩn theo chính sách.')
        change(430, 'Ký số cá nhân:', 'Ký nháy chiều đi: Sign Service xác định người ký từ JWT và tài khoản đang hoạt động, kiểm tra đơn vị và bước chờ ký. Công văn chung do Lãnh đạo Phòng cấp Trường ký; Admin hỗ trợ bằng chứng thư của mình. Không tin signerId hoặc tên người ký do trình duyệt tự khai.')
        change(431, 'Ký số pháp nhân', 'Ký số pháp nhân: Ban Giám hiệu ký khi công văn đi đã ký nháy và được trình đúng bước; Admin có quyền hỗ trợ. Sign Service ghi chữ ký theo tài khoản thực hiện. Danh sách Đã ký dựa trên bản ghi Signatures của tài khoản, không dựa vào trạng thái Đã ký của toàn bộ hồ sơ.')
        change(435, 'Truy xuất và Ghi đè', 'Truy xuất và lưu PDF: Sign Service đọc đúng MinIO object của hồ sơ và lưu nội dung đã ký. Chiều đến giữ tệp nguồn, bản ký chỉ đạo và bản ký phân công riêng theo luồng hiện có; chiều đi lưu PDF đã ký tại object được quản lý. Không mặc định coi mọi lần sửa tệp là một phiên bản có thể khôi phục.')
        change(436, 'Cập nhật trạng thái', 'Cập nhật trạng thái: Chiều đi ký PDF và ghi Signatures trước, sau đó Document Service kiểm tra bằng chứng chữ ký để hoàn tất DeptSigned hoặc DirectorSigned. Nếu bước cập nhật lỗi, người ký có thể thử Hoàn tất lại mà không ký lặp. Hai lệnh chưa phải một giao dịch nguyên tử xuyên dịch vụ; phát hành vẫn là bước riêng của Văn thư.')
        change(478, 'Quy trình mã hóa nội bộ:', 'Quy trình ký số: Sign Service sử dụng thuật toán băm để tính dấu vân tay nội dung và khóa trong chứng thư để tạo chữ ký. Việc xác minh giúp kiểm tra tính toàn vẹn PDF và chứng thư theo cơ chế đã triển khai; không đồng nhất thao tác ký với việc dán ảnh chữ ký.')
        change(482, 'Lưu vết vật lý', 'Lưu vết vật lý trên MinIO: Hồ sơ tham chiếu đến tệp nguồn và bản ký theo luồng xử lý. Chiều đến giữ các bản cần đối chiếu; chiều đi dùng object PDF được Sign Service quản lý. Lịch sử nghiệp vụ được lưu riêng; chưa có kho phiên bản hoàn chỉnh để khôi phục mọi lần thay tệp.')
        change(483, 'Nhất quán dữ liệu', 'Nhất quán dữ liệu: Chữ ký được lưu trước khi hoàn tất trạng thái ký chiều đi; API chỉ chấp nhận người có thẩm quyền và bằng chứng ký phù hợp. Khi cập nhật trạng thái thất bại sau ký, có bước thử hoàn tất lại. Published chỉ được ghi khi Văn thư phát hành sau ký, không tự chuyển từ yêu cầu ký PDF.')
        change(494, 'Sơ đồ ERD', 'Sơ đồ ERD dưới đây thể hiện các quan hệ logic và trường dữ liệu chính, bổ sung đơn vị người tạo, cờ công văn chung và đơn vị xử lý chiều đến. Quan hệ logic trong sơ đồ không có nghĩa mọi liên kết giữa các dịch vụ đều đã được tạo thành khóa ngoại vật lý; ràng buộc cụ thể được mô tả ở từng bảng.')
        change(535, 'Định hướng luồng xử lý', 'Định hướng luồng xử lý: Đơn vị người tạo quyết định phạm vi gốc của công văn. Với chiều đến, Ban Giám hiệu chọn thêm đơn vị xử lý; Lãnh đạo Phòng phải thuộc đúng đơn vị được chỉ đạo mới được ký phân công. Đơn vị xử lý và đơn vị tạo là hai thông tin khác nhau.')
        change(536, 'Phân quyền dữ liệu:', 'Phân quyền dữ liệu: Tài khoản xem công văn trong đơn vị mình, công văn chung và các hồ sơ trực tiếp tạo, được giao hoặc đã ký; Ban Giám hiệu/Admin xem toàn trường. Chưa có quy tắc tự động cho mọi đơn vị cha được xem toàn bộ hồ sơ riêng của đơn vị con.')
        change(538, 'Bảng này đóng vai trò', 'Bảng Documents lưu thông tin hồ sơ, trạng thái, kết quả OCR, người tạo, phạm vi đơn vị và liên kết tệp MinIO. Đơn vị nguồn của hồ sơ được chụp tại thời điểm tạo, tách khỏi đơn vị đang xử lý công văn đến.')
        cell(539,2,4,'Số hiệu công văn do người có quyền nhập hoặc đối soát, không tự ghi đè khi chạy OCR.')
        cell(539,3,4,'Tiêu đề hoặc trích yếu do người có quyền nhập và hoàn thiện theo hồ sơ.')
        cell(539,5,4,'Đường dẫn tới tệp PDF, ảnh hoặc Word trong MinIO; bước ký số cần PDF phù hợp.')
        cell(539,6,4,'Kết quả nội dung/bố cục OCR và thông tin Word hiệu chỉnh, gắn với phiên bản nguồn.')
        cell(539,7,4,'Trạng thái theo hai luồng, ví dụ Draft, PendingDeptReview, DeptSigned, PendingDirectorSign, DirectorSigned, Published; chiều đến có PendingDirection, PendingAssignment, IncomingAssigned.')
        table=blocks[539]; template=table.findall('w:tr',NS)[-1]
        fields=[
            ('created_by_id','GUID','Tham chiếu tài khoản','Người tạo từ danh tính xác thực; không lấy từ client.'),
            ('created_at','DateTime UTC','Ghi khi tạo','Thời gian tạo; giao diện hiển thị giờ Việt Nam.'),
            ('department_id','GUID','Có thể NULL ở hồ sơ cũ','Đơn vị người tạo tại lúc tạo, bất biến theo hồ sơ.'),
            ('department_name','Varchar(255)','Bản chụp tên đơn vị','Tên đơn vị của hồ sơ tại lúc tạo.'),
            ('is_school_wide','Boolean','Mặc định False','Công văn chung khi người tạo thuộc đơn vị cấp Trường.'),
            ('processing_department_id','GUID','Có thể NULL','Đơn vị xử lý chiều đến do BGH chỉ đạo, khác đơn vị nguồn.')]
        for i,values in enumerate(fields,9):
            row=copy.deepcopy(template)
            for c,v in zip(row.findall('w:tc',NS),[str(i),*values]):set_cell(c,v)
            table.append(row)
        header=table.find('w:tr',NS);pr=header.find('w:trPr',NS)
        if pr is None:pr=E.SubElement(header,W+'trPr')
        if pr.find('w:tblHeader',NS) is None:E.SubElement(pr,W+'tblHeader')
        change(542, 'Tích hợp OCR Service:', 'Tích hợp OCR: Worker chỉ lưu kết quả khi nguồn và giai đoạn còn hợp lệ. OCR không tự ghi đè tiêu đề, số hiệu hoặc ngày ban hành do người dùng đang quản lý. Tải Word hiệu chỉnh lưu riêng thông tin người sửa, thời điểm và lần sửa, không thay PDF đã ký.')
        change(548, 'Thiết kế dữ liệu bổ sung', 'Dữ liệu người tạo và vòng đời đã triển khai: CreatedById, CreatedAt, FirstSubmittedAt, FirstAssignedAt, OfficiallyReceivedAt, SigningStartedAt và ConcurrencyVersion hỗ trợ xác định giai đoạn, quyền xóa và kiểm soát đồng thời. Phải đối chiếu thêm lịch sử và chữ ký, không chỉ dựa vào Draft.')
        change(549, 'Đề xuất bổ sung', 'Dữ liệu xóa mềm gồm IsDeleted, DeletedAt, DeletedById, DeleteReason và lịch sử thao tác. Đơn vị hồ sơ gồm DepartmentId, DepartmentName, IsSchoolWide; migration ngày 05/11/2026 suy đơn vị hồ sơ cũ từ đơn vị hiện tại của người tạo. Thiếu thông tin thì giữ chưa xác định để Admin đối soát, không tự gán công văn chung.')
        change(550, 'Giữ liên kết Documents', 'Giữ hồ sơ, lịch sử và tệp khi xóa mềm hoặc hủy. Xóa vĩnh viễn là thao tác Admin riêng, có xác nhận và nhật ký độc lập; tệp không dọn được ngay sẽ được xử lý lại. Dữ liệu cũ có thể thiếu bằng chứng về người tạo hoặc giai đoạn, cần đối soát trước khi cấp quyền khôi phục.')
        change(575, 'Quy trình gồm:', 'Quy trình gồm: Văn thư chọn ảnh/PDF của hồ sơ còn Nháp hoặc Trả sửa trong phạm vi được phép ở cả hai chiều; chọn model và tối đa 5 trang; PaddleOCR/PP-Structure cung cấp bằng chứng chữ/bảng; Gemini sinh JSON bố cục; lưu kết quả và xuất Word. Văn thư đối chiếu, sửa ngoài ứng dụng và tải DOCX hiệu chỉnh; hệ thống ghi người sửa/thời gian. Word hoàn chỉnh không OCR.')
        change(584, 'Đến ngày 25/10/2026', 'Đến ngày 05/11/2026, đã có chọn model Gemini, cấu hình API key tại Admin, OCR nền qua Kafka, thông báo và cập nhật realtime theo phạm vi hồ sơ. Văn thư hỗ trợ OCR/tải Word hiệu chỉnh ảnh/PDF ở Nháp hoặc Trả sửa trong cả hai chiều; Chuyên viên không được làm hai thao tác này. Giữ Word hiệu chỉnh hiện hành và lịch sử, không có trình soạn thảo Word trong ứng dụng hoặc kho mọi phiên bản cũ.')
        change(590, 'Đề tài giữ hai luồng:', 'Đề tài giữ hai luồng: chiều đến do Văn thư tạo/tiếp nhận, OCR và trình BGH ký chỉ đạo, Lãnh đạo Phòng đúng đơn vị ký phân công, Chuyên viên nhận xử lý; chiều đi do Chuyên viên soạn/trình, Lãnh đạo Phòng cùng đơn vị ký nháy, BGH ký pháp nhân, Văn thư phát hành/lưu trữ. Văn thư được hỗ trợ OCR/hiệu chỉnh ảnh/PDF Nháp/Trả sửa cả hai chiều; không thay vai trò tác giả và không phân công chiều đi.')
        change(593, 'Ngày 25/10/2026', 'Đến ngày 05/11/2026, công văn có phạm vi đơn vị người tạo và cờ công văn chung, nhãn/bộ lọc trong danh sách. Admin/BGH xem toàn trường; các vai trò khác xem theo đơn vị và quan hệ hồ sơ. Mục Ký số có Chờ ký theo bước/thẩm quyền và Đã ký theo tài khoản, thời điểm, liên kết hồ sơ; Trưởng phòng cấp Trường xử lý công văn chung.')
        change(594, 'Chiều đến đã có', 'Chiều đến đã có hai bước ký số và trạng thái riêng. Chiều đi đã nối ký PDF với kiểm tra bằng chứng trước cập nhật DeptSigned/DirectorSigned, có cơ chế thử hoàn tất khi cập nhật lỗi. Thông báo chờ ký nháy chỉ gửi đúng đơn vị; SSE, tệp và OCR kiểm tra cùng phạm vi. Kết quả kiểm thử kỹ thuật không thay thế nghiệm thu nghiệp vụ.')
        change(597, 'Chiều đi cần tiếp tục', 'Giới hạn cần kiểm thử tiếp: Hai yêu cầu ký PDF và hoàn tất trạng thái chiều đi chưa nằm trong một giao dịch nguyên tử xuyên dịch vụ; cần nghiệm thu tình huống mất kết nối, chứng thư thật và phát hành sau ký. Đơn vị hồ sơ cũ được suy từ dữ liệu tài khoản hiện tại, không chứng minh đơn vị tại thời điểm tạo; hồ sơ thiếu thông tin cần Admin đối soát. Upload không còn tự phát luồng OCR cũ; phải gửi yêu cầu OCR riêng.')
        change(598, 'Kiểm thử bộ lọc', 'Kiểm thử ngày 05/11/2026: DocumentService có 218 kiểm thử đạt, 28 bỏ qua; SignService có 30 đạt, 2 bỏ qua; Gateway có 36 đạt, 2 bỏ qua. Có kiểm tra phân quyền đơn vị, giả mạo người ký, danh sách đã ký, thông báo/SSE và giao diện 1366/300px bằng dữ liệu kiểm thử. Các trường hợp bỏ qua chưa được xem là đạt; chưa ký tài liệu thật bằng chứng thư người dùng trong lượt này.')

        # Reuse the source diagram generator and visual language, never its main().
        spec=importlib.util.spec_from_file_location('thesis_diagrams',BASE/'tools/update_thesis_role_workflows.py')
        diagrams=importlib.util.module_from_spec(spec);spec.loader.exec_module(diagrams);diagrams.PROOF=PROOF
        media={
            'word/media/image2.png':diagrams.role_diagram('Chi tiết chức năng Văn thư',846,471,'Văn thư',[
                'Tiếp nhận chiều đến\nGắn đơn vị tài khoản','OCR ảnh/PDF hai chiều\nNháp hoặc Trả sửa',
                'Trình BGH chỉ đạo\nKhông phân công','Phát hành sau ký\nTheo phạm vi được phép'], 'Văn thư\nĐơn vị mình\nvà công văn chung'),
            'word/media/image4.png':diagrams.role_diagram('Chi tiết chức năng Lãnh đạo Phòng',904,470,'Lãnh đạo Phòng',[
                'Chờ ký / Đã ký\nTheo tài khoản','Trả sửa Chuyên viên\nđã soạn dự thảo',
                'Ký nháy đúng đơn vị\nChung: cấp Trường','Ký phân công chiều đến\nĐúng đơn vị chỉ đạo'], 'Lãnh đạo Phòng\nĐúng đơn vị\nvà giai đoạn')
        }
        media['word/media/image11.png']=diagrams.sequence('ocr_sequence_scope.png',1830,775,
            ['Văn thư / UI','API Gateway','Document Service\nWorker / Outbox','PostgreSQL','Kafka','OCR Service\nPaddle + Gemini','MinIO'],[
            ('Giai đoạn 1: Lưu hồ sơ và gửi yêu cầu OCR riêng',),
            (0,1,'1. Tạo chiều đến / tải tệp; ghi đơn vị người tạo'),
            (1,2,'2. Kiểm tra phạm vi; lưu nguồn'),
            (2,6,'3. Ảnh/PDF/Word; Word hoàn chỉnh không OCR'),
            (0,2,'4. Chọn model / tối đa 5 trang; hai chiều Nháp/Trả sửa'),
            (2,3,'5. Kiểm tra quyền / giai đoạn / nguồn; lưu OcrJobs'),
            (2,0,'6. HTTP 202; tiếp tục thao tác'),
            ('Giai đoạn 2: Worker xử lý nền và thông báo theo phạm vi',),
            (2,4,'7. Outbox phát JobId'),(4,2,'8. Worker nhận và claim tác vụ'),
            (2,5,'9. Gọi nội bộ sau khi kiểm tra lại'),(5,6,'10. Đọc nguồn ảnh/PDF'),
            (5,2,'11. Trả JSON nội dung / bố cục'),
            (2,3,'12. Lưu kết quả / lịch sử / thông báo'),
            (2,0,'13. SSE cho người xem hợp lệ; báo người yêu cầu xong/lỗi'),
            (0,2,'14. Tải Word / tải DOCX hiệu chỉnh; không gọi lại AI')])
        media['word/media/image12.png']=diagrams.sequence('sign_sequence_scope.png',1619,758,
            ['Chuyên viên','Lãnh đạo Phòng','Ban Giám hiệu','Văn thư','Gateway /\nDocument Service','Sign Service','MinIO /\nPostgreSQL'],[
            ('Chiều đi: Ký đúng đơn vị; công văn chung do cấp Trường ký nháy',),
            (0,4,'1. Tạo / trình; lưu đơn vị người tạo'),
            (1,4,'2. Chờ ký đúng đơn vị; duyệt hoặc trả sửa tác giả'),
            (1,5,'3. Ký nháy PDF qua Gateway'),
            (5,6,'4. Lưu PDF / chữ ký'),
            (1,4,'5. Hoàn tất DeptSigned; thử lại nếu lỗi; trình BGH'),
            (2,5,'6. Ký pháp nhân đúng bước; lưu chữ ký'),
            (2,4,'7. Hoàn tất DirectorSigned; không tự phát hành'),
            (3,4,'8. Văn thư đúng phạm vi phát hành / lưu trữ'),
            ('Chiều đến: Hai chữ ký, đơn vị xử lý do BGH chỉ đạo',),
            (3,4,'9. Tiếp nhận / OCR; trình BGH chỉ đạo'),
            (2,5,'10. Ký chỉ đạo; chọn đơn vị xử lý'),
            (5,6,'11. Chờ phân công'),
            (1,5,'12. Lãnh đạo đúng đơn vị ký phân công'),
            (5,6,'13. Ghi nhận phân công'),
            (4,0,'14. Chuyên viên hợp lệ nhận công văn được giao')])
        media['word/media/image13.png']=scope_erd()

        settings=E.fromstring(zin.read('word/settings.xml'))
        update=settings.find('w:updateFields',NS)
        if update is None:update=E.SubElement(settings,W+'updateFields')
        update.set(W+'val','true')
        settings_xml=E.tostring(settings,xml_declaration=True,encoding='UTF-8',standalone=True)

        xml=E.tostring(root,xml_declaration=True,encoding='UTF-8',standalone=True)
        with ZipFile(OUTPUT,'w') as zout:
            for info in zin.infolist():
                data=xml if info.filename=='word/document.xml' else settings_xml if info.filename=='word/settings.xml' else media.get(info.filename,zin.read(info.filename))
                zout.writestr(copy.copy(info),data)
        with ZipFile(OUTPUT) as zout:
            untouched=[n for n in zin.namelist() if n not in ('word/document.xml','word/settings.xml') and n not in media]
            assert zin.namelist()==zout.namelist()
            assert all(zin.read(n)==zout.read(n) for n in untouched)
        assert [E.tostring(n) for n in before.findall('.//w:sectPr',NS)]==[E.tostring(n) for n in root.findall('.//w:sectPr',NS)]
        doc=Document(OUTPUT)
        assert len(doc.tables)==17
        assert len(doc.tables[8].rows)==26
        result={'source':str(SOURCE.relative_to(BASE)),'output':str(OUTPUT.relative_to(BASE)),
            'source_sha256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),'output_sha256':hashlib.sha256(OUTPUT.read_bytes()).hexdigest(),
            'tables':len(doc.tables),'matrix_rows':len(doc.tables[8].rows),'changes':changes,'updated_diagrams':list(media),
            'unchanged_package_parts':len(untouched),'sections_preserved':True,'request_field_update_on_open':True,'render_verified':False}
        (PROOF/'verification.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
        print(json.dumps({k:v for k,v in result.items() if k!='changes'},ensure_ascii=False))

if __name__=='__main__':main()
