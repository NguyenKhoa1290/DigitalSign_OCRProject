"""Synchronize the reviewed thesis with the approved incoming/outgoing roles.

Run in the isolated document container. Preserve the source, existing styles,
sections, headers, footers and unrelated package parts. No application mutation.
"""
import copy
import hashlib
import json
import sys
from pathlib import Path
from zipfile import ZipFile

from docx import Document
from lxml import etree as E
from PIL import Image, ImageDraw, ImageFont

BASE = Path('/work')
SOURCE = BASE / 'tai_lieu/05_bao_cao/Do_an_tot_nghiep_cap_nhat_phan_quyen_xoa.docx'
OUTPUT = BASE / 'tai_lieu/05_bao_cao/Do_an_tot_nghiep_cap_nhat_luong_va_phan_quyen_20261025.docx'
PROOF = BASE / 'tai_nguyen/kiem_tra_bao_cao/luong_vai_tro_20261025'
NS = {'w': 'http://schemas.openxmlformats.org/wordprocessingml/2006/main'}
W = '{' + NS['w'] + '}'
PROOF.mkdir(parents=True, exist_ok=True)


def text(n):
    return ''.join(n.xpath('.//w:t/text()', namespaces=NS))


def set_text(p, value):
    nodes = p.findall('.//w:t', NS)
    assert nodes, value[:80]
    first = nodes[0]
    first.text = value
    first.set('{http://www.w3.org/XML/1998/namespace}space', 'preserve')
    for n in nodes[1:]:
        n.text = ''
    # Keep a bold introductory label without making the whole paragraph bold.
    run = first.getparent()
    bold = run.find('w:rPr/w:b', NS)
    normal = next((n.getparent() for n in nodes[1:]
                   if n.getparent().find('w:rPr/w:b', NS) is None), None)
    if bold is not None and normal is not None and ':' in value:
        label, content = value.split(':', 1)
        first.text = label + ':'
        new_run = E.Element(W + 'r')
        rpr = normal.find(W + 'rPr')
        if rpr is not None:
            new_run.append(copy.deepcopy(rpr))
        t = E.SubElement(new_run, W + 't')
        t.text = content
        t.set('{http://www.w3.org/XML/1998/namespace}space', 'preserve')
        run.addnext(new_run)


def paragraph_like(template, value):
    p = copy.deepcopy(template)
    for n in list(p):
        if n.tag != W + 'pPr':
            p.remove(n)
    r = E.SubElement(p, W + 'r')
    old_run = template.find(W + 'r')
    if old_run is not None and old_run.find(W + 'rPr') is not None:
        r.append(copy.deepcopy(old_run.find(W + 'rPr')))
    E.SubElement(r, W + 't').text = value
    return p


def set_cell(cell, lines):
    paras = cell.findall(W + 'p')
    template = next((p for p in paras if text(p)), paras[0])
    for p in paras:
        cell.remove(p)
    for value in lines:
        cell.append(paragraph_like(template, value))


def h(p):
    return hashlib.sha256(p.read_bytes()).hexdigest()


# Deterministic diagrams: same blue/white appearance and canvas dimensions as
# the source, with Vietnamese text rendered from a Unicode font at 2x density.
FONT = '/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf'
BOLD = '/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf'
SCALE = 2


def canvas(width, height):
    im = Image.new('RGB', (width * SCALE, height * SCALE), 'white')
    return im, ImageDraw.Draw(im)


def label(d, xy, value, size=14, bold=False, anchor='mm'):
    font = ImageFont.truetype(BOLD if bold else FONT, size * SCALE)
    d.multiline_text((xy[0]*SCALE, xy[1]*SCALE), value, font=font,
                     fill='#111111', anchor=anchor, align='center', spacing=2*SCALE)


def line(d, a, b, width=1, dashed=False):
    if dashed:
        length = ((b[0]-a[0])**2+(b[1]-a[1])**2)**.5
        if not length:
            return
        for start in range(0, int(length), 10):
            end = min(start+5, length)
            pts = [(a[0]+(b[0]-a[0])*t/length, a[1]+(b[1]-a[1])*t/length) for t in (start, end)]
            d.line([(x*SCALE,y*SCALE) for x,y in pts], fill='#404040', width=width*SCALE)
    else:
        d.line([(a[0]*SCALE,a[1]*SCALE),(b[0]*SCALE,b[1]*SCALE)], fill='#333333', width=width*SCALE)


def arrow(d, a, b, dashed=False):
    import math
    line(d,a,b,dashed=dashed)
    angle = math.atan2(b[1]-a[1],b[0]-a[0])
    for sign in [-1,1]:
        end=(b[0]-8*math.cos(angle+sign*.45),b[1]-8*math.sin(angle+sign*.45))
        line(d,b,end)


def role_diagram(name, width, height, actor, actions, central):
    im,d=canvas(width,height)
    bottom=height-94
    d.rectangle((7*SCALE,7*SCALE,(width-7)*SCALE,bottom*SCALE),outline='#222222',width=SCALE)
    label(d,(width/2,20),name,14,True)
    cx,cy=width*.5,bottom*.66
    rx,ry=width*.145,bottom*.19
    d.ellipse(((cx-rx)*SCALE,(cy-ry)*SCALE,(cx+rx)*SCALE,(cy+ry)*SCALE),fill='#87cefa',outline='#69b3df',width=SCALE)
    label(d,(cx,cy),central,13,True)
    # Four associated use cases; login is the top use case.
    positions=[(width*.18,bottom*.45),(width*.18,bottom*.79),(width*.82,bottom*.45),(width*.82,bottom*.79)]
    for (x,y),value in zip(positions,actions):
        erx,ery=width*.16,bottom*.075
        d.ellipse(((x-erx)*SCALE,(y-ery)*SCALE,(x+erx)*SCALE,(y+ery)*SCALE),fill='#87cefa',outline='#69b3df',width=SCALE)
        label(d,(x,y),value,13)
        ax=x+erx if x<cx else x-erx
        bx=cx-rx if x<cx else cx+rx
        arrow(d,(ax,y),(bx,cy),dashed=True)
    x,y=cx,bottom*.23
    d.ellipse(((x-55)*SCALE,(y-23)*SCALE,(x+55)*SCALE,(y+23)*SCALE),fill='#87cefa',outline='#69b3df',width=SCALE)
    label(d,(x,y),'Đăng nhập',13)
    arrow(d,(cx,cy-ry),(cx,y+23))
    ax,ay=width*.15,bottom+28
    d.ellipse(((ax-7)*SCALE,(ay-7)*SCALE,(ax+7)*SCALE,(ay+7)*SCALE),outline='#444444',width=SCALE)
    line(d,(ax,ay+7),(ax,ay+32));line(d,(ax-14,ay+16),(ax+14,ay+16))
    line(d,(ax,ay+32),(ax-12,ay+46));line(d,(ax,ay+32),(ax+12,ay+46))
    label(d,(ax,ay+60),actor,13)
    line(d,(ax+24,ay+15),(cx-rx,cy+ry*.65))
    path=PROOF/('updated_'+name.split()[0]+str(width)+'.png')
    im.save(path)
    return path.read_bytes()


def sequence(filename,width,height,participants,events):
    im,d=canvas(width,height)
    xs=[110+(width-220)*i/(len(participants)-1) for i in range(len(participants))]
    for x,p in zip(xs,participants):
        d.rectangle(((x-100)*SCALE,12*SCALE,(x+100)*SCALE,78*SCALE),fill='#e2e2ef',outline='#777777',width=SCALE)
        label(d,(x,45),p,18)
        line(d,(x,73),(x,height-45),dashed=True)
        label(d,(x,height-23),p,15)
    ys=[108+(height-177)*i/(len(events)-1) for i in range(len(events))]
    for y,event in zip(ys,events):
        if len(event)==1:
            d.rectangle((0,(y-13)*SCALE,width*SCALE,(y+13)*SCALE),fill='#f2f2f2',outline='#555555',width=SCALE)
            label(d,(width/2,y),event[0],21,True)
        else:
            a,b,value=event
            if a==b:
                line(d,(xs[a],y),(xs[a]+26,y));line(d,(xs[a]+26,y),(xs[a]+26,y+12));arrow(d,(xs[a]+26,y+12),(xs[a],y+12))
                label(d,(xs[a]+32,y-9),value,12,anchor='lm')
            else:
                arrow(d,(xs[a],y+8),(xs[b],y+8))
                font=ImageFont.truetype(FONT,22*SCALE)
                half=d.textlength(value,font=font)/SCALE/2
                cx=max(half+8,min(width-half-8,(xs[a]+xs[b])/2))
                d.rectangle(((cx-half-3)*SCALE,(y-17)*SCALE,(cx+half+3)*SCALE,(y+6)*SCALE),fill='white')
                label(d,(cx,y-4),value,22)
    path=PROOF/filename
    im.save(path)
    return path.read_bytes()


def main():
    assert SOURCE.resolve()!=OUTPUT.resolve()
    if OUTPUT.exists() and '--overwrite-draft' not in sys.argv:
        raise RuntimeError('Preserve the existing reviewed output; choose a new output or remove only a confirmed draft.')
    with ZipFile(SOURCE) as zin:
        root=E.fromstring(zin.read('word/document.xml'))
        body=root.find(W+'body')
        before=copy.deepcopy(root)
        paras=[p for p in body.findall(W+'p') if not p.xpath('./w:pPr/w:pStyle[starts-with(@w:val,"TOC")]',namespaces=NS)]
        changed=[]
        def find(prefix):
            found=[p for p in paras if text(p).startswith(prefix)]
            assert len(found)==1,(prefix,len(found))
            return found[0]
        def change(prefix,value):
            p=find(prefix)
            changed.append((root.getroottree().getpath(p),text(p),value))
            set_text(p,value)

        change('Tiếp nhận và Số hóa: Tiếp nhận công văn giấy',
               'Tiếp nhận và Số hóa: Văn thư tạo hồ sơ công văn đến, tải ảnh/PDF gốc và đối soát thông tin trước khi trình Ban Giám hiệu chỉ đạo. Văn thư không phân công Chuyên viên xử lý.')
        change('Vận hành OCR: Sử dụng dịch vụ nhận dạng',
               'Vận hành OCR: Văn thư chọn model Gemini và tối đa 5 trang mỗi lượt của ảnh/PDF đã tải lên. PaddleOCR hỗ trợ nhận diện; mô hình ngôn ngữ lớn dựng bố cục. Văn thư đối chiếu, sửa Word bên ngoài và tải lại bản DOCX hoàn chỉnh. Word tải lên không cần OCR.')
        change('Phát hành và Lưu trữ: Thực hiện các thủ tục',
               'Phát hành và Lưu trữ: Văn thư kiểm tra công văn đi đã hoàn tất ký cấp phòng và ký pháp nhân, sau đó phát hành và lưu trữ. Không soạn, trình duyệt hoặc sửa nội dung dự thảo chiều đi thay Chuyên viên; chữ ký pháp nhân do Ban Giám hiệu thực hiện trước phát hành.')
        change('Soạn thảo: Khởi tạo các tờ trình, dự thảo',
               'Soạn thảo: Chuyên viên tạo công văn đi, chuẩn bị nội dung hoàn chỉnh bằng trình soạn thảo bên ngoài, tải tệp và nhập thông tin hồ sơ rồi nộp trình duyệt. Chuyên viên không được chạy OCR; khi lãnh đạo yêu cầu sửa, người đã soạn cập nhật và trình lại.')
        change('Kiểm soát chuyên môn: Xem xét và chỉnh sửa',
               'Kiểm soát chuyên môn: Xem xét dự thảo công văn đi, duyệt hoặc yêu cầu Chuyên viên đã soạn sửa lại; không trực tiếp thay nội dung và không chuyển Văn thư sửa thay Chuyên viên.')
        change('Vận hành OCR: Hệ thống tự động nhận diện',
               'Vận hành OCR: Văn thư gửi yêu cầu số hóa công văn đến bằng PaddleOCR và mô hình ngôn ngữ lớn Gemini; theo dõi kết quả chạy nền, kiểm tra và hiệu chỉnh Word nhận diện. Chuyên viên không có quyền chạy OCR.')
        change('Phát hành và Lưu trữ: Thực hiện đóng dấu',
               'Phát hành và Lưu trữ: Văn thư phát hành công văn đi sau khi đã có các chữ ký theo quy trình và lưu trữ hồ sơ. Thao tác phát hành không thay thế bước Ban Giám hiệu ký pháp nhân.')
        change('Tiếp nhận và Số hóa: Khi Văn thư chọn',
               'Tiếp nhận và Số hóa: Văn thư tạo hồ sơ công văn đến và tải ảnh/PDF gốc lên MinIO. Tải tệp và yêu cầu OCR là hai thao tác riêng; Word hoàn chỉnh được lưu và tải xuống, không OCR.')
        change('Hệ thống sử dụng OCR Service để tự động nhận diện',
               'Khi Văn thư gửi yêu cầu, OCR Service xử lý các trang ảnh/PDF đã chọn bằng PaddleOCR kết hợp Gemini; kết quả nội dung và bố cục được lưu gắn hồ sơ, không tự ghi đè metadata đang có.')
        change('Người dùng thực hiện hiệu chỉnh trực tiếp các thông tin',
               'Văn thư đối chiếu thông tin hồ sơ và Word nhận diện với bản gốc, sửa lỗi bằng trình soạn thảo ngoài rồi tải DOCX hoàn chỉnh lên. Kết quả tự động không được coi là chính xác tuyệt đối hoặc thay thế tệp nguồn đã ký.')
        change('Phát hành và Lưu trữ: Sau khi văn bản',
               'Phát hành và Lưu trữ: Công văn đi đã được Lãnh đạo Phòng ký nháy và Ban Giám hiệu ký pháp nhân được chuyển cho Văn thư phát hành. Hệ thống ghi trạng thái Đã ban hành, lịch sử và lưu trữ; Văn thư không ký pháp nhân thay Ban Giám hiệu.')
        change('Kiểm tra bóc tách dự thảo:',
               'Hoàn thiện và trình dự thảo: Chuyên viên nhập, kiểm tra thông tin của công văn đi, tải nội dung hoàn chỉnh và trình lãnh đạo phòng. Không kích hoạt OCR khi Chuyên viên tải dự thảo. Khi bị trả sửa, Chuyên viên đã soạn sửa tệp/thông tin rồi trình lại.')
        change('Kiểm duyệt và Chỉnh sửa dự thảo:',
               'Kiểm duyệt và Yêu cầu chỉnh sửa: Lãnh đạo Phòng xem xét dự thảo của Chuyên viên. Nếu cần sửa, nhập lý do và trả về Chuyên viên đã soạn; khi nội dung đạt yêu cầu mới ký xác nhận cấp phòng.')

        tables=body.findall(W+'tbl')
        def usecase(index,key,lines):
            rows=tables[index].findall(W+'tr')
            row=next(r for r in rows if text(r.findall(W+'tc')[0]).strip()==key)
            cell=row.findall(W+'tc')[1]
            changed.append((root.getroottree().getpath(cell),text(cell),'\n'.join(lines)))
            set_cell(cell,lines)
        usecase(1,'Hậu điều kiện',[
            'Tệp nguồn được lưu trên MinIO; kết quả OCR gắn với hồ sơ và có thông báo hoàn tất/lỗi. Văn thư đối chiếu, hiệu chỉnh trước khi trình BGH. OCR không tự chuyển trạng thái tiếp nhận hoặc ký.'])
        usecase(1,'Kịch bản chính',[
            '1. Văn thư tạo hồ sơ công văn đến, nhập thông tin cơ bản và lưu bản nháp.',
            '2. Tải ảnh/PDF lên; hệ thống lưu tệp nguồn trên MinIO và cung cấp xem trước. Với Word hoàn chỉnh, bỏ qua bước OCR.',
            '3. Tại chi tiết công văn, chọn Chạy OCR, chọn model Gemini và tối đa 5 trang mỗi lượt của tệp đã tải lên.',
            '4. Gateway xác thực; Document Service kiểm tra quyền, nguồn tệp, phiên bản và tác vụ đang chạy, lưu OcrJobs rồi trả HTTP 202.',
            '5. Outbox phát JobId qua Kafka; worker gọi OCR Service nội bộ. PaddleOCR cung cấp chữ/cấu trúc hỗ trợ Gemini dựng bố cục JSON.',
            '6. Document Service lưu kết quả, lịch sử và thông báo thành công/lỗi. Người dùng được tiếp tục thao tác; trang đang mở nhận cập nhật realtime.',
            '7. Văn thư đối chiếu nội dung, tải Word, sửa bằng trình soạn thảo ngoài và tải bản DOCX hoàn chỉnh vào kết quả OCR. Hệ thống ghi người sửa, thời gian và lần sửa; giữ tệp gốc.',
            '8. Văn thư hoàn thiện thông tin, chuẩn bị PDF cho ký số và chọn Trình BGH chỉ đạo. Đây là thao tác riêng với OCR.'])
        usecase(1,'Ngoại lệ',[
            'Chưa có tệp, tệp không hỗ trợ, quá dung lượng hoặc quá 5 trang: từ chối và yêu cầu chọn lại; không gọi AI.',
            'Hết hạn mức hoặc lỗi dịch vụ: tác vụ báo lỗi, giữ kết quả cũ; Văn thư có thể chọn model khác và gửi lại.',
            'Nguồn tệp thay đổi hoặc hồ sơ bị xóa trong khi chạy: không gắn kết quả cũ vào nguồn mới.',
            'Thiếu PDF phù hợp: chưa cho trình BGH/ký. Bản Word hiệu chỉnh không tự thay thế PDF nguồn.'])
        usecase(2,'Tiền điều kiện',[
            'Chuyên viên đã đăng nhập; tạo công văn đi và chuẩn bị nội dung hoàn chỉnh. Admin có thể hỗ trợ theo quyền đã cấp.'])
        usecase(2,'Kịch bản chính',[
            '1. Chuyên viên chọn Tạo công văn mới và loại công văn đi.',
            '2. Nhập thông tin hồ sơ và lưu bản nháp; không gọi OCR.',
            '3. Tải tệp dự thảo hoàn chỉnh lên MinIO; với bước ký số, cần PDF phù hợp.',
            '4. Kiểm tra nội dung, tệp đính kèm và thông tin cần thiết trước khi trình.',
            '5. Chuyên viên đã tạo hồ sơ chọn Nộp trình duyệt để chuyển tới Lãnh đạo Phòng.',
            '6. Hệ thống kiểm tra quyền, người tạo và giai đoạn, lưu trạng thái/lịch sử và thông báo kết quả.',
            '7. Khi lãnh đạo trả sửa, Chuyên viên đã soạn cập nhật nội dung rồi trình lại theo bước duyệt quy định; không chuyển Văn thư sửa thay.'])
        usecase(2,'Ngoại lệ',[
            'Không phải người tạo hoặc không đủ quyền: từ chối thao tác trình; hồ sơ thiếu bằng chứng người tạo cần Admin đối soát.',
            'Thiếu thông tin/tệp cần thiết hoặc tệp không hỗ trợ: giữ bản nháp, yêu cầu hoàn thiện trước bước tương ứng.',
            'Tệp dùng để ký không phải PDF hoặc nội dung thay đổi sau ký: phải hoàn thiện và kiểm tra lại, không tự coi chữ ký cũ còn giá trị.',
            'Lỗi MinIO/API: báo lỗi, không thông báo trình duyệt thành công khi backend từ chối.'])
        usecase(3,'Hậu điều kiện',[
            'Chiều đến: PDF có chữ ký phân công của Lãnh đạo Phòng sau chữ ký chỉ đạo BGH, chuyển IncomingAssigned. Chiều đi: dự thảo được ký nháy và chuyển tiếp theo quy trình duyệt.'])
        usecase(3,'Kịch bản chính',[
            '1. Lãnh đạo Phòng mở công văn thuộc phạm vi đơn vị và xem PDF/lịch sử.',
            '2. Chiều đến: kiểm tra hồ sơ PendingAssignment và chữ ký chỉ đạo BGH còn hợp lệ; chọn Chuyên viên đang hoạt động thuộc đơn vị hoặc đơn vị trực thuộc, nhập yêu cầu.',
            '3. Lãnh đạo Phòng ký phân công IncomingAssignment. Chỉ khi ký và xác minh thành công mới chuyển IncomingAssigned, ghi lịch sử và thông báo người nhận.',
            '4. Chiều đi: xem dự thảo do Chuyên viên trình; đạt yêu cầu thì ký nháy, sau đó chuyển BGH theo các bước duyệt.',
            '5. Nếu cần sửa nội dung chiều đi, nhập lý do và trả về Chuyên viên đã soạn; không trực tiếp sửa thay hoặc chuyển Văn thư.',
            '6. Hệ thống kiểm tra vai trò, phạm vi đơn vị, phiên bản và chữ ký trước khi ghi nhận kết quả.'])
        usecase(3,'Ngoại lệ',[
            'Thiếu/sai người nhận hoặc ngoài phạm vi đơn vị: không phân công.',
            'PDF thiếu chữ ký BGH hợp lệ, chứng thư lỗi hoặc ký thất bại: giữ giai đoạn cũ, không báo phân công thành công.',
            'Phiên bản thay đổi đồng thời: trả xung đột và yêu cầu tải lại.',
            'Thời hạn, báo cáo tiến độ và duyệt hoàn thành là phạm vi cần hoàn thiện; không coi đã có chỉ từ bước ký phân công.'])
        usecase(4,'Tiền điều kiện',[
            'Ban Giám hiệu đã đăng nhập. Chiều đến: hồ sơ PendingDirection có PDF phù hợp. Chiều đi: dự thảo đã hoàn tất kiểm soát và ký nháy cấp phòng.'])
        usecase(4,'Hậu điều kiện',[
            'Chiều đến: ký chỉ đạo thành công chuyển PendingAssignment cho đúng đơn vị. Chiều đi: ký pháp nhân thành công chuyển DirectorSigned; Văn thư phát hành riêng mới thành Published.'])
        usecase(4,'Kịch bản chính',[
            '1. Ban Giám hiệu mở công văn, kiểm tra chiều, PDF, lịch sử và phiên bản.',
            '2. Chiều đến: chọn đơn vị, nhập chỉ đạo và ký IncomingDirection. Thành công mới chuyển PendingAssignment, lưu bản ký và thông báo đúng đơn vị.',
            '3. Lãnh đạo Phòng tiếp tục ký phân công theo mục 2.1.4.4; hai bước của chiều đến đều ký số.',
            '4. Chiều đi: kiểm tra chữ ký nháy, dùng chứng thư của mình thực hiện Ký số pháp nhân.',
            '5. Hệ thống xác minh chữ ký, lưu bản PDF đã ký riêng, ghi lịch sử và chuyển DirectorSigned khi thành công.',
            '6. Văn thư kiểm tra bản đã ký, phát hành và lưu trữ; hệ thống mới ghi Published. Không ký pháp nhân lần nữa tại thao tác phát hành.'])
        usecase(4,'Ngoại lệ',[
            'Chiều đi cần sửa: BGH ghi lý do và trả lại theo cấp duyệt; Chuyên viên đã soạn là người cập nhật nội dung, không giao Văn thư sửa thay.',
            'Lỗi chứng thư, thiếu chữ ký cần có hoặc xác minh không đạt: không chuyển thành đã ký/phát hành.',
            'Phiên bản thay đổi hoặc sai giai đoạn: từ chối và yêu cầu kiểm tra lại.',
            'Admin không được ký thay chỉ nhờ vai trò quản trị; quyền hỗ trợ không bỏ qua vai trò và điều kiện ký.'])

        # Split ambiguous aggregate permissions; keep each existing row's styles.
        matrix=tables[8]
        header=matrix.findall(W+'tr')[0]
        header_pr=header.find(W+'trPr')
        if header_pr is None:
            header_pr=E.Element(W+'trPr');header.insert(0,header_pr)
        if header_pr.find(W+'tblHeader') is None:
            E.SubElement(header_pr,W+'tblHeader')
        # The existing narrow Admin column otherwise wraps the final letter.
        admin_props=header.findall(W+'tc')[1].find(W+'tcPr')
        margins=admin_props.find(W+'tcMar')
        if margins is None:
            margins=E.SubElement(admin_props,W+'tcMar')
        for side in ('left','right'):
            edge=margins.find(W+side)
            if edge is None:
                edge=E.SubElement(margins,W+side)
            edge.set(W+'w','0');edge.set(W+'type','dxa')
        def matrix_row(prefix):
            return next(r for r in matrix.findall(W+'tr') if text(r.findall(W+'tc')[0]).startswith(prefix))
        def row_values(row,values):
            for c,v in zip(row.findall(W+'tc'),values):set_cell(c,[v])
            for n in row.findall('./w:trPr/w:trHeight',NS):n.getparent().remove(n)
        def insert_rows_after(anchor,values):
            for offset,vals in enumerate(values,1):
                r=copy.deepcopy(anchor);row_values(r,vals)
                matrix.insert(matrix.index(anchor)+offset,r)
        row_values(matrix_row('Tiếp nhận công văn đến'),['Tạo/tiếp nhận công văn đến','X (4)','X','','',''])
        old=matrix_row('Soạn thảo, tải công văn lên')
        row_values(old,['Soạn, tải và trình công văn đi','X (4)','','X','',''])
        insert_rows_after(old,[['Yêu cầu Chuyên viên sửa dự thảo','','','','X','X']])
        row_values(matrix_row('Phân phối và lưu trữ'),['Phát hành/lưu trữ công văn đi','X (4)','X','','',''])
        ocr=matrix_row('Kiểm tra kết quả bóc tách')
        row_values(ocr,['Chạy OCR công văn đến','X (4)','X','','',''])
        insert_rows_after(ocr,[
            ['Hiệu chỉnh Word OCR chiều đến','X (4)','X','','',''],
            ['Xem nội dung/kết quả được phép','X','X','X','X','X'],
            ['Ký chỉ đạo công văn đến','','','','','X'],
            ['Ký phân công công văn đến','','','','X',''],
        ])
        # Do not strand a single permission line or a group label on another page.
        for row in matrix.findall(W+'tr'):
            props=row.find(W+'trPr')
            if props is None:
                props=E.Element(W+'trPr');row.insert(0,props)
            if props.find(W+'cantSplit') is None:
                E.SubElement(props,W+'cantSplit')
            if row is header or text(row.findall(W+'tc')[0]).startswith('Nhóm '):
                for p in row.findall('.//w:p',NS):
                    props_p=p.find(W+'pPr')
                    if props_p is None:
                        props_p=E.Element(W+'pPr');p.insert(0,props_p)
                    if props_p.find(W+'keepNext') is None:
                        E.SubElement(props_p,W+'keepNext')
        matrix_intro=find('Ma trận phân quyền:')
        props_p=matrix_intro.find(W+'pPr')
        if props_p is None:
            props_p=E.Element(W+'pPr');matrix_intro.insert(0,props_p)
        if props_p.find(W+'keepNext') is None:
            E.SubElement(props_p,W+'keepNext')
        change('Bổ sung phân quyền ngày 19/10/2026:',
               'Ma trận được cập nhật ngày 25/10/2026: tách tạo/trình công văn đi, tiếp nhận/OCR chiều đến, xem kết quả và ký chỉ đạo/phân công. Đây là phân quyền nghiệp vụ đã chốt; trạng thái triển khai và giới hạn xem tại mục 2.8. Các chú thích (1), (2), (3) tiếp tục áp dụng cho xóa mềm dự thảo.')
        anchor=find('(3) Admin được khôi phục')
        body.insert(body.index(anchor)+1,paragraph_like(anchor,
            '(4) Admin hỗ trợ tạo/trình chiều đi và tiếp nhận/OCR theo quyền đã thống nhất; không ký thay lãnh đạo hoặc bỏ qua điều kiện ký. Chuyên viên chỉ trình dự thảo của mình; Văn thư OCR/hiệu chỉnh chiều đến, phát hành chiều đi. Quyền xem kết quả không bao gồm chạy OCR. Việc giới hạn Văn thư sửa/OCR trên công văn đi còn cần đồng bộ trong mã nguồn.'))
        # Role summary is consistent with the finer matrix.
        role_table=tables[7]
        for row in role_table.findall(W+'tr'):
            cells=row.findall(W+'tc')
            if text(cells[0])=='Văn thư':set_cell(cells[1],['Tạo/tiếp nhận công văn đến, OCR và hiệu chỉnh; trình BGH chỉ đạo; phát hành/lưu trữ chiều đi sau duyệt/ký.'])
            if text(cells[0])=='Chuyên viên':set_cell(cells[1],['Soạn và trình công văn đi; sửa khi bị trả lại; xử lý công văn đến được giao. Không chạy OCR.'])

        change('Tiếp nhận tín hiệu bất đồng bộ (Event-driven):',
               'Tiếp nhận yêu cầu bất đồng bộ: Văn thư gửi lệnh OCR từ tệp đã lưu, chọn model và tối đa 5 trang. Document Service lưu tác vụ và trả HTTP 202; outbox phát JobId qua Kafka. Worker kiểm tra tác vụ, quyền và nguồn trước khi gọi OCR Service nội bộ. Không gửi JWT hay API key trong thông điệp hàng đợi.')
        change('Quy trình bóc tách dữ liệu (Extraction Pipeline):',
               'Quy trình bóc tách dữ liệu: OCR Service đọc ảnh/PDF nguồn từ MinIO theo thông tin server xác minh. PaddleOCR/PP-Structure cung cấp chữ và cấu trúc hỗ trợ; Gemini nhận ảnh cùng bằng chứng để sinh JSON bố cục. Kết quả không được mặc định chính xác và cần Văn thư đối chiếu.')
        change('Trích xuất Metadata:',
               'Nội dung và thông tin hồ sơ: JSON giữ nội dung nhận diện, bố cục và các điểm chưa chắc chắn. Văn thư đối chiếu và hoàn thiện metadata; OCR không tự ghi đè số hiệu, ngày ban hành hoặc trích yếu đang lưu của công văn.')
        change('Số hóa và Lưu trữ bền vững:',
               'Lưu kết quả và thông báo: Worker của Document Service kiểm tra nguồn còn khớp, lưu JSON, lịch sử và thông báo cùng giao dịch PostgreSQL. Lỗi tác vụ giữ kết quả cũ; thay tệp hoặc xóa hồ sơ không được gắn kết quả nguồn cũ. SSE cập nhật nội dung cho người đang mở hồ sơ; chỉ người yêu cầu nhận thông báo hoàn tất/lỗi chưa đọc.')
        change('Tính bất đồng bộ (Asynchronous):',
               'Tính bất đồng bộ: Tải tệp chỉ lưu nguồn; yêu cầu OCR được gửi riêng khi Văn thư chọn model/trang. API lưu tác vụ và trả HTTP 202 ngay, cho phép người dùng thao tác tiếp. Kafka phân phối JobId cho worker; trạng thái tác vụ được lưu bền trong PostgreSQL.')
        change('Sự phối hợp đa ngôn ngữ:',
               'Phối hợp dịch vụ: Gateway kiểm tra truy cập; Document Service quản lý tác vụ, đồng thời và thông báo; OCR Service Python thực hiện PaddleOCR kết hợp Gemini. Nguồn tệp do server xác minh, không nhận đường dẫn tùy ý từ client.')
        change('Hạ tầng hạ tầng và lưu trữ:',
               'Lưu trữ và cập nhật: Document Service ghi kết quả nhận diện, lịch sử và thông báo sau khi kiểm tra lại nguồn. SSE cập nhật trang chi tiết và chuông thông báo. Xuất hoặc tải lại Word đã hiệu chỉnh không gọi AI; Word chỉnh sửa không thay tệp nguồn hoặc chữ ký.')
        change('Quy trình gồm: tiếp nhận ảnh gốc;',
               'Quy trình gồm: Văn thư tiếp nhận ảnh/PDF công văn đến; chọn model và tối đa 5 trang mỗi lượt; PaddleOCR/PP-Structure cung cấp bằng chứng chữ/bảng; Gemini sinh JSON bố cục từ ảnh và bằng chứng; kiểm tra dữ liệu; lưu kết quả gắn hồ sơ; xuất Word để đối chiếu. Văn thư sửa bằng trình soạn thảo ngoài và tải DOCX hoàn chỉnh lên, hệ thống ghi người sửa và thời gian. Word hoàn chỉnh tải lên không cần OCR.')
        change('Phương án đã được lựa chọn, thử nghiệm bằng script',
               'Đến ngày 25/10/2026, đã có giao diện chọn model Gemini, cấu hình API key tại Admin, OCR nền qua Kafka, thông báo kết quả và cập nhật realtime tại chi tiết công văn. Bản Word hiệu chỉnh được gắn với hồ sơ, có tên người sửa, thời gian và lần sửa; giữ Word hiện hành và lịch sử thao tác, không lưu mọi phiên bản tệp cũ. Chưa có trình soạn thảo Word trong ứng dụng. Phạm vi Văn thư chỉ OCR/hiệu chỉnh chiều đến là quyết định nghiệp vụ còn cần đồng bộ đầy đủ quyền API/giao diện. Chất lượng OCR trên bộ tài liệu thật vẫn cần nghiệm thu.')
        change('Phạm vi đề tài giữ nguyên hai luồng',
               'Đề tài giữ hai luồng: chiều đến do Văn thư tạo/tiếp nhận, OCR và hiệu chỉnh, trình BGH ký chỉ đạo, chuyển Lãnh đạo Phòng ký phân công rồi giao Chuyên viên xử lý; chiều đi do Chuyên viên soạn và trình, Lãnh đạo Phòng ký nháy, BGH ký pháp nhân, Văn thư phát hành/lưu trữ. Nếu cần sửa chiều đi, trả về Chuyên viên đã soạn. Chuyên viên không chạy OCR; Văn thư không phân công và không sửa dự thảo chiều đi thay Chuyên viên.')
        change('Tại thời điểm rà soát ngày 13/10/2026,',
               'Ngày 25/10/2026, ứng dụng có phân loại/lọc hồ sơ, lưu PDF/ảnh/Word, metadata và lịch sử. Chuyên viên tạo chiều đi, Văn thư tạo chiều đến, Admin hỗ trợ cả hai. Trình chiều đi kiểm tra vai trò và người tạo; Chuyên viên không được chạy OCR.')
        change('Document Service hỗ trợ chuyển trạng thái trình duyệt,',
               'Chiều đến đã có ký chỉ đạo BGH và ký phân công cấp phòng, với các mốc PendingDirection, PendingAssignment, IncomingAssigned. Chiều đi giữ ký nháy, ký pháp nhân và phát hành riêng. OCR nền có thông báo, cập nhật realtime và Word hiệu chỉnh gắn hồ sơ. Kiểm thử kỹ thuật không thay thế nghiệm thu nghiệp vụ trên tài liệu thật.')
        change('Luồng công văn đến cần hoàn thiện thao tác bút phê riêng,',
               'Chiều đến đã có ký chỉ đạo/chuyển đơn vị và ký phân công; chưa có bút phê hiển thị thành vùng chữ trên PDF, quản lý thời hạn, báo cáo kết quả hoặc duyệt hoàn thành. IncomingAssigned chỉ xác nhận đã giao xử lý. Cần nghiệm thu chứng thư, PDF và đúng đơn vị/người nhận trên tài liệu thật.')
        change('Luồng công văn đi cần liên kết kết quả ký PDF',
               'Chiều đi cần tiếp tục kiểm tra liên kết PDF/chữ ký với trạng thái, quyền, phiên bản và điều kiện phát hành. Quyền Văn thư sửa/tải thay hoặc OCR công văn đi còn cần đồng bộ ở giao diện/API. Upload tệp không phải Word hiện vẫn phát sự kiện document.uploaded của luồng Paddle cũ; cần rà soát và giới hạn nhánh tự động này để dự thảo chiều đi không bị OCR sau upload. Quyền chặn Chuyên viên ở API chạy OCR chưa chứng minh đã tắt mọi đường tự động. Không coi các phần này đã hoàn thành chỉ vì thiết kế đã cập nhật; không tự chuyển dữ liệu cũ sang workflow mới.')
        change('Kiểm thử kỹ thuật bộ lọc ngày 13/10/2026',
               'Kiểm thử bộ lọc ngày 13/10/2026 tại 1366px và 390px xác nhận tham số, phân trang và loại văn bản, không chứng minh hoàn tất hai luồng nghiệp vụ. Người dùng cần nghiệm thu công văn thật với đầy đủ vai trò; căn cứ cũ là TC-DOC-TYPE-FILTER-069.')
        change('Kết quả nghiệm thu cần ghi rõ tài liệu đầu vào,',
               'Biên bản nghiệm thu cần ghi đầu vào, người thực hiện, các bước, đầu ra, ngày chạy và lỗi còn lại. OCR phải đối chiếu Word với nguồn; ký số phải kiểm tra PDF, chữ ký và trạng thái hồ sơ. Nghiệm thu người dùng chưa được thay bằng kiểm thử kỹ thuật.')
        # The old deletion baseline is explicitly dated, avoiding a false claim
        # that the current application still performs hard deletion of drafts.
        change('Đối chiếu mã nguồn hiện tại: API còn xóa cứng',
               'Nhận xét API xóa cứng và chưa có nút xóa là kết quả rà soát ngày 19/10/2026, không còn mô tả phiên bản hiện tại. Đến ngày 25/10/2026, mã nguồn đã có xóa mềm/khôi phục bản nháp theo điều kiện và chức năng Admin hủy hồ sơ có lý do; xóa vĩnh viễn là thao tác riêng cho hồ sơ đã xóa. Hủy của Admin không đồng nghĩa quyền xóa bản nháp thông thường được bỏ qua điều kiện nghiệp vụ.')
        change('Nghiệm thu phải kiểm tra vai trò và người tạo,',
               'Nghiệm thu tiếp tục kiểm tra vai trò, người tạo, giai đoạn, xung đột với trình/ký, khôi phục và dọn tệp khi xóa vĩnh viễn. Phải phân biệt xóa mềm dự thảo, Admin hủy và xóa vĩnh viễn; không ghi nhận nghiệm thu người dùng chỉ từ kiểm thử kỹ thuật. Hồ sơ cũ thiếu bằng chứng người tạo/giai đoạn cần đối soát. Chính sách thời hạn lưu giữ và thu hồi bên ngoài hệ thống chưa được suy ra từ các thao tác này.')

        # Update only diagrams whose labels/sequence contradict the approved flow.
        media={}
        media['word/media/image2.png']=role_diagram('Chi tiết chức năng Văn thư',846,471,'Văn thư',[
            'Tiếp nhận chiều đến\nTải ảnh/PDF/Word','OCR và hiệu chỉnh\nWord chiều đến',
            'Trình BGH chỉ đạo\nKhông phân công','Phát hành chiều đi\nLưu trữ hồ sơ'],
            'Văn thư\nTiếp nhận và OCR\nPhát hành / lưu trữ')
        media['word/media/image4.png']=role_diagram('Chi tiết chức năng Lãnh đạo Phòng',904,470,'Lãnh đạo Phòng',[
            'Duyệt dự thảo\nCông văn đi','Yêu cầu Chuyên viên\nsửa lại dự thảo',
            'Ký nháy\nCông văn đi','Ký phân công\nCông văn đến'],
            'Lãnh đạo Phòng\nDuyệt và ký\nPhân công chiều đến')
        media['word/media/image5.png']=role_diagram('Chi tiết chức năng Ban Giám hiệu',943,475,'Ban Giám hiệu',[
            'Chọn đơn vị\nNhập chỉ đạo','Ký chỉ đạo\nCông văn đến',
            'Duyệt cuối cùng\nCông văn đi','Ký pháp nhân\nCông văn đi'],
            'Ban Giám hiệu\nKý chỉ đạo chiều đến\nDuyệt / ký chiều đi')
        media['word/media/image11.png']=sequence('ocr_sequence_updated.png',1830,775,
            ['Văn thư / UI','API Gateway','Document Service\nWorker / Outbox','PostgreSQL','Kafka','OCR Service\nPaddle + Gemini','MinIO'],[
            ('Giai đoạn 1: Lưu nguồn và gửi yêu cầu OCR riêng',),
            (0,1,'1. Tải ảnh/PDF; tạo hồ sơ chiều đến'),
            (1,2,'2. Xác thực / chuyển yêu cầu'),
            (2,6,'3. Lưu tệp nguồn'),
            (0,2,'4. Chọn model / tối đa 5 trang, gửi OCR qua Gateway'),
            (2,3,'5. Kiểm tra quyền / phiên bản; lưu OcrJobs'),
            (2,0,'6. HTTP 202; người dùng tiếp tục thao tác'),
            ('Giai đoạn 2: Kafka phân phối, worker xử lý và thông báo',),
            (2,4,'7. Outbox phát JobId'),
            (4,2,'8. Worker nhận và claim tác vụ'),
            (2,5,'9. Gọi nội bộ sau kiểm tra lại nguồn / quyền'),
            (5,6,'10. Đọc ảnh/PDF nguồn'),
            (5,2,'11. Paddle + Gemini trả JSON bố cục'),
            (2,3,'12. Lưu kết quả / lịch sử / thông báo cùng giao dịch'),
            (2,0,'13. SSE cập nhật; người yêu cầu nhận thông báo xong / lỗi'),
            (0,2,'14. Tải Word / tải DOCX đã hiệu chỉnh; không gọi AI'),
        ])
        media['word/media/image12.png']=sequence('sign_sequence_updated.png',1619,758,
            ['Chuyên viên','Lãnh đạo Phòng','Ban Giám hiệu','Văn thư','Gateway /\nDocument Service','Sign Service','MinIO /\nPostgreSQL'],[
            ('Chiều đi: Chuyên viên soạn, lãnh đạo ký, Văn thư phát hành',),
            (0,4,'1. Tải dự thảo / nộp trình duyệt; không OCR'),
            (1,4,'2. Duyệt hoặc trả sửa cho Chuyên viên đã soạn'),
            (1,5,'3. Ký nháy qua Gateway'),
            (5,6,'4. Lưu PDF / chữ ký / trạng thái sau xác minh'),
            (2,5,'5. Kiểm tra và ký pháp nhân qua Gateway'),
            (5,6,'6. Lưu bản ký; trạng thái DirectorSigned'),
            (3,4,'7. Phát hành sau đủ các bước duyệt / ký'),
            (4,6,'8. Published, lịch sử và lưu trữ'),
            ('Chiều đến: Hai bước ký riêng, không dùng nhánh phát hành chiều đi',),
            (3,4,'9. Tiếp nhận / OCR / đối soát; Trình BGH chỉ đạo'),
            (2,5,'10. Ký chỉ đạo và chọn đơn vị (IncomingDirection)'),
            (5,6,'11. Thành công: PendingAssignment'),
            (1,5,'12. Chọn Chuyên viên, ký IncomingAssignment'),
            (5,6,'13. Thành công: IncomingAssigned / thông báo'),
            (4,0,'14. Nhận công văn được giao xử lý'),
        ])
        xml=E.tostring(root,xml_declaration=True,encoding='UTF-8',standalone=True)
        with ZipFile(OUTPUT,'w') as zout:
            for info in zin.infolist():
                data=xml if info.filename=='word/document.xml' else media.get(info.filename,zin.read(info.filename))
                zout.writestr(copy.copy(info),data)
        with ZipFile(OUTPUT) as zout:
            untouched=[n for n in zin.namelist() if n!='word/document.xml' and n not in media]
            assert all(zin.read(n)==zout.read(n) for n in untouched)
            assert zin.namelist()==zout.namelist()
        # Every original section/page setup and all original paragraph properties remain.
        assert [E.tostring(n) for n in before.findall('.//w:sectPr',NS)]==[E.tostring(n) for n in root.findall('.//w:sectPr',NS)]
        doc=Document(OUTPUT)
        assert len(doc.tables)==17
        assert len(doc.tables[8].rows)==22
        assert not any('kích hoạt OCR Service' in p.text for p in doc.tables[2].rows[5].cells[1].paragraphs)
        out=BASE/'tai_nguyen/noi_dung_trich_xuat'
        (out/'doc_content.txt').write_text('\n'.join(p.text for p in doc.paragraphs),encoding='utf-8')
        (out/'tables_content.txt').write_text('\n\n'.join(
            f'=== TABLE {i} ===\n'+'\n'.join(' | '.join(c.text.strip() for c in r.cells).rstrip() for r in t.rows)
            for i,t in enumerate(doc.tables))+'\n',encoding='utf-8')
        (out/'source.json').write_text(json.dumps({'source':str(OUTPUT.relative_to(BASE)),
            'source_sha256':h(OUTPUT),'updated_date':'2026-10-25','tables':len(doc.tables),
            'paragraphs':len(doc.paragraphs)},ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
        result={'source':str(SOURCE.relative_to(BASE)),'output':str(OUTPUT.relative_to(BASE)),
            'source_sha256':h(SOURCE),'output_sha256':h(OUTPUT),'tables':len(doc.tables),
            'matrix_rows':len(doc.tables[8].rows),'updated_diagrams':list(media),
            'unchanged_package_parts':len(untouched),'sections_preserved':True,
            'changes':[{'path':p,'before':a,'after':b} for p,a,b in changed],
            'render_verified':False}
        (PROOF/'verification.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
        print(json.dumps({k:v for k,v in result.items() if k!='changes'},ensure_ascii=False))


if __name__=='__main__':
    main()
