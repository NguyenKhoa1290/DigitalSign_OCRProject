# FlightTaskbarWeb

Phiên bản web độc lập của thanh `LiquidBottomTabs` trong template Kotlin gốc
`../AndroidLiquidGlass-kmp/AndroidLiquidGlass-kmp`.
Trang thư viện có 13 trang: Taskbar, Nút bấm, Cần gạt & slider, Filter tự động,
Control Center, Dialog, Lock Screen, Kính lúp, Progressive Blur, Scroll,
Lazy Scroll, Playground và Catalog. Bao phủ các destination trong Kotlin gốc;
Toggle và Slider dùng chung một trang web. Taskbar giữ 5 icon máy bay, nhãn `Tab 1`–`Tab 5`
và bảng điều chỉnh bên dưới.
Không phụ thuộc React, thư viện UI, CDN hay backend. JavaScript/CSS/SVG được đóng gói trực tiếp;
Playwright chỉ là dependency phát triển để kiểm thử trên trình duyệt.
Inter được đóng gói cục bộ (Latin và Vietnamese subset), nên không cần tải font từ CDN khi chạy web hoặc resource kit. Giấy phép SIL Open Font License 1.1 nằm tại `INTER-OFL.txt`.

## Chạy và build

Yêu cầu Node.js 20 trở lên.

```powershell
cd E:\Android\FlightTaskbarWeb
npm run dev
# http://127.0.0.1:5173

npm run build
npm run preview
```

Build tạo `dist/`, có thể triển khai lên bất kỳ static host nào. Chạy bằng HTTP, không mở
`index.html` bằng `file://`, vì mã dùng ES modules. Dev/preview server chỉ bind localhost.

## Component resources

Đọc từ template Kotlin **đầy đủ**, không từ bản đã lược bỏ. Mỗi trang có preview tương tác,
ví dụ HTML/JS để sao chép, link module web và bản Kotlin gốc được phục vụ tại `resources/kotlin/`.
Hash URL (`#buttons`, `#switches`, `#adaptive`, `#control-center`) hỗ trợ mở thẳng và Back/Forward.
Chuyển trang tháo component cũ, dừng spring và giải phóng renderer; không giữ các trang ẩn với nhiều context WebGL.

- `src/components/liquid-button.js`: LiquidButton gốc, 48px, blur 2, lens 12/24,
  scale nhấn `1 + 4/48`; native button hỗ trợ bàn phím, disabled, sự kiện `activate`.
- `src/components/liquid-toggle.js`: biến thể web compact của LiquidToggle gốc: track **56×28**,
  thumb **36×24**, khoảng chạy 16px, nhưng vẫn có vùng chạm cao 44px; giảm chiều dài theo yêu cầu.
  Scale nhấn 1.5, blur `8×(1-progress)`, lens `5/10×progress` được giữ nguyên.
  Nhấn/kéo, RTL và huỷ gesture; `checked`, `change.detail.checked`.
- `src/components/liquid-slider.js`: LiquidSlider gốc, track 6, thumb 40×24,
  lens `10/14×progress`, scale nhấn 1.5. Native range có min/max/step, disabled,
  bàn phím; `value`, `input/change.detail.value`. Slider dọc là phần mở rộng cho web.
- `src/components/glass-surface.js`: lớp kính dùng lại với slot DOM, shader WebGL2
  và CSS fallback. `configure(...)`, `backdrop`, `refresh(true)`; dùng `<adaptive-glass>`
  để bật filter tự động.
- `src/components/backdrop.js`: công thức trong
  [AdaptiveLuminanceGlassContent.kt](../AndroidLiquidGlass-kmp/AndroidLiquidGlass-kmp/app/src/commonMain/kotlin/com/kyant/backdrop/catalog/destinations/AdaptiveLuminanceGlassContent.kt).
  Lấy mẫu 5×5, luminance `.2126R + .7152G + .0722B`, signed-square remapping,
  brightness/contrast/blur theo các lerp gốc, saturation 1.5 và đổi màu chữ ở `.5`.
  Liên tục lấy mẫu mỗi giây và tween luminance 1s với easing mặc định của Compose;
  brightness/contrast/blur được tính từ giá trị đang tween. Màu chữ chuyển 1s.
  Hỗ trợ kéo, pinch zoom và xoay như màn gốc. `filterchange.detail` và `filterState` cung cấp thông số.
- `src/components/control-center.js`: bố cục từ
  [ControlCenterContent.kt](../AndroidLiquidGlass-kmp/AndroidLiquidGlass-kmp/app/src/commonMain/kotlin/com/kyant/backdrop/catalog/destinations/ControlCenterContent.kt):
  item 68, span đôi 152, gap 16, bo 34, lens 24/48, depth, dim nền .4;
  spring mở/đóng stiffness 300 và damping .5/1. Handle kéo dọc để thu/mở.
  Bản gốc chủ yếu là layout; web bổ sung nút trạng thái, media demo và slider dọc.
  Đã có đủ 11 ô kính ngoài, gồm hàng cuối. Góc highlight thay đổi theo trọng lực với
  smoothing .5, falloff 2. Nút cảm biến là opt-in; browser cần hỗ trợ DeviceMotion và
  secure context (localhost/HTTPS), một số browser yêu cầu cấp quyền. Thiếu quyền/dữ liệu
  vẫn dùng thanh chỉnh góc thủ công. Không đổi Wi-Fi, âm lượng, brightness hệ thống hay phát nhạc thật.
  API `open`, `state`, `controlchange.detail`.
- `liquid-dialog.js`: radius48, lens24/48, blur16 sáng /8 tối, bảng màu và bố cục gốc.
  Native modal có focus trap, Escape, Cancel/Okay, trả focus; `show()`, `close()`, `dialogaction.detail.action`.
- `liquid-lock-screen.js`: **texture `clock_sdf.webp` chép nguyên byte từ template**,
  chuyển SdfShader.kt sang GLSL; brightness−.1, contrast.75, saturation1.5, blur2,
  refraction48, dim.3. Pointer/keyboard drag; `reset()`.
- `liquid-magnifier.js`: lens128×96, zoom1.5, cursor4×24 cách80, lens8/24,
  depth/chromatic, inner shadow16. Canvas2D vẽ text mẫu và cursor vào cùng capture nền;
  chữ/con trỏ được phóng đại thực, không dùng bản text giả trong lens. `text`, `theme`, `reset()`.
- `progressive-blur.js`: cao128, blur4 cố định, tint.8, alpha `1−smoothstep(64,128,y)`.
  Đây là alpha-masked blur như nguồn gốc, không phải bán kính blur thay đổi theo vị trí.
- `glass-scroll.js`: 20 thẻ thường hoặc `<glass-scroll lazy>` với100 thẻ được virtualize.
  Height160, gap16, radius32, lens16/32. Dùng một renderer/context WebGL chung cho các
  thẻ nhìn thấy; không tạo một context cho mỗi thẻ. `count`, `lazy`, `renderedRange`.
- `glass-playground.js`: ô256px, 5 slider nguyên khoảng/default, kéo/pinch/xoay,
  ẩn/hiện sheet và reset có spring. Phím mũi tên di chuyển, +/- zoom, [/] xoay,
  Ctrl+wheel zoom, Shift+wheel xoay. Shader bù transform khi lấy mẫu backdrop.
- Trang Catalog giữ tên và ba nhóm HomeContent.kt gốc; các mục liên kết tới demo tương ứng.

```js
import { setGlassBackdrop } from './src/components/index.js';
// Cùng canvas/image phủ viewport với CSS nền trang. Gọi lại sau khi vẽ lại nền.
setGlassBackdrop(backgroundCanvas);
```

### Lấy tài nguyên

`npm run build` tạo `resources.zip` và `dist/resources.zip`: chỉ module component cùng
dependencies, một trang mẫu độc lập, LICENSE/NOTICE, README sử dụng và `sources.json`.
Không cần gallery/demo JS để dùng component. Gói có 58 file, gồm 25 bản Kotlin tham chiếu nguyên bản
trong `original-kotlin/`; đây là nguồn đối chiếu, không phải project Android build độc lập.
Build lấy chúng từ thư mục template cạnh project; nếu không có template thì vẫn đóng gói module web.
Nút **Tải bộ source** tải ZIP; nút **Sao chép ví dụ** sao chép code của trang hiện tại.

Giới hạn: lens/dispersion chạy WebGL2; CSS fallback chỉ blur/tint và tương tác.
Blur web là xấp xỉ 9 mẫu. GlassSurface giữ nội dung DOM riêng, không khúc xạ text slot;
capture phải là canvas/image cùng origin hoặc có CORS. Không tự đọc DOM phía sau.
Magnifier dùng capture canvas tường minh cho nội dung demo; khi tích hợp nội dung ứng dụng
khác phải cung cấp/vẽ capture tương ứng. Clock fallback giữ alpha texture gốc, magnifier
fallback giữ phóng đại canvas nhưng không có khúc xạ SDF/lens hay quang sai WebGL.
DeviceMotion đã kiểm thử bằng event giả lập; cần thử riêng trên thiết bị thật để xác nhận cảm biến.
Đây là chuyển thông số/công thức và tương tác, không cam kết pixel-identical với Android.

## Bảng điều chỉnh

Nghiên cứu từ bản Kotlin đầy đủ, không dùng các file đã loại bỏ trong `FlightTaskbar`:

- [GlassPlaygroundContent.kt](../AndroidLiquidGlass-kmp/AndroidLiquidGlass-kmp/app/src/commonMain/kotlin/com/kyant/backdrop/catalog/destinations/GlassPlaygroundContent.kt):
  5 thanh chính giữ đúng khoảng giá trị gốc: bo góc `0..1`, blur `0..32`,
  vùng khúc xạ `0..1`, độ lệch khúc xạ `0..1`, tách màu `0..1`.
- [LiquidBottomTabs.kt](../AndroidLiquidGlass-kmp/AndroidLiquidGlass-kmp/app/src/commonMain/kotlin/com/kyant/backdrop/catalog/components/LiquidBottomTabs.kt)
  và [DampedDragAnimation.kt](../AndroidLiquidGlass-kmp/AndroidLiquidGlass-kmp/app/src/commonMain/kotlin/com/kyant/backdrop/catalog/utils/DampedDragAnimation.kt):
  thông số mặc định của taskbar và chuyển động nhấn/kéo.
- [ColorFilter.kt](../AndroidLiquidGlass-kmp/AndroidLiquidGlass-kmp/backdrop/src/commonMain/kotlin/com/kyant/backdrop/effects/ColorFilter.kt),
  [Highlight.kt](../AndroidLiquidGlass-kmp/AndroidLiquidGlass-kmp/backdrop/src/commonMain/kotlin/com/kyant/backdrop/highlight/Highlight.kt),
  [Shadow.kt](../AndroidLiquidGlass-kmp/AndroidLiquidGlass-kmp/backdrop/src/commonMain/kotlin/com/kyant/backdrop/shadow/Shadow.kt):
  độ bão hoà, viền sáng và bóng đổ.

Mở **Ánh sáng & chuyển động** để chỉnh thêm 9 thuộc tính: độ phủ kính, saturation, viền sáng,
bóng đổ, phóng to khi nhấn, stiffness và 3 damping ratio. Giá trị mặc định lấy từ Kotlin;
khoảng chỉnh của 9 thanh mở rộng được chọn cho bản web, không phải các slider có sẵn trong playground gốc.

Hai bộ thông số được tách riêng:

- **Mặc định**: giữ taskbar gốc — bo góc `1`, blur `8`, vùng khúc xạ `.75`,
  độ lệch `.375`, tách màu bật, chiều sâu tắt. Với chiều cao 64px,
  công thức `height = fraction × 64 × .5`, `amount = fraction × 64` cho lens `24/24px`.
- **Thông số playground gốc**: `.5, 0, .2, .2, 0`, chiều sâu bật.
  Giữ các tỷ lệ gốc nhưng áp dụng lên taskbar 64px, thay cho ô vuông 256dp trong Kotlin.

Tách màu giữ đúng điều kiện Kotlin `chromaticAberration > 0f`: `0` là tắt, mọi giá trị dương là bật,
không phải cường độ liên tục. Trên taskbar, hiệu ứng này xuất hiện ở ô chọn khi nhấn/kéo.
Checkbox **Khúc xạ chiều sâu** điều khiển pháp tuyến của lens.

Kéo slider cập nhật shader/spring ngay, không dựng lại renderer. Thông số và nền được lưu trong
localStorage; chọn nền **Sọc** để quan sát khúc xạ rõ hơn. Không có WebGL2 thì các điều chỉnh
khúc xạ/tách màu/chiều sâu bị vô hiệu hoá, còn blur và tương tác vẫn dùng được.

## Đối chiếu với mã Android

- `LiquidBottomTabs.kt`, `LiquidBottomTab.kt` → `src/flight-taskbar.js`:
  5 tab, capsule 64px, padding 4px, selector 56px, icon 28px, chữ 12px, gap 2px.
  Nhấn tab để chọn; nhấn giữ selector đang chọn để kéo và thả vào tab gần nhất.
- `DampedDragAnimation.kt` → `src/spring.js` và các spring trong component:
  vị trí/press `(stiffness=1000, damping=1)`, scale X `(250, 0.6)`,
  scale Y `(250, 0.7)`, pressed scale `78/56`, velocity `(300, 0.5)`.
  Panel có dịch chuyển tối đa 4px; selector biến dạng theo vận tốc như mã gốc.
- `InteractiveHighlight.kt` → shader glow: cường độ `0.08 + 0.15`,
  bán kính 1.5 lần chiều cao, spring `(300, 0.5)`.
- `backdrop/.../internal/Shaders.kt` → `src/shaders.js`:
  capsule SDF, hàm `circleMap`, gradient pháp tuyến, khúc xạ panel 24px/24px,
  khúc xạ selector 10px/14px theo mức nhấn, tách màu 7 mẫu và highlight 45°.
- `backdrops/LayerBackdrop.kt`, `internal/InverseLayerScope.kt` → bù scale khi lấy mẫu
  nền và icon qua ô chọn. Scale kính `78/56` không nhân vào scale icon; icon chỉ có
  scale `1 → 1.2` từ `LocalLiquidBottomTabScale`. Lớp tabs được giới hạn trong capsule
  cao 56px như bản Kotlin, tạo viền quang sai khi các mẫu màu đi qua ranh giới này.
- `effects/ColorFilter.kt` → saturation `1.5` với luminance weights `0.213/0.715/0.072`.
- `FlightIcon.kt` → `src/flight-icon.js`: giữ nguyên SVG path trong viewport 960×960.
- `BackdropDemoScaffold.kt` → nền `#9AD7FF → #DAF0FF → #B9D4BC`;
  nền và taskbar được capture/render riêng để không tạo vòng lặp render.

Các file Kotlin ở `../AndroidLiquidGlass-kmp/AndroidLiquidGlass-kmp/app/src/commonMain/kotlin/com/kyant/backdrop/catalog/`;
shader và hiệu ứng ở `../AndroidLiquidGlass-kmp/AndroidLiquidGlass-kmp/backdrop/src/commonMain/kotlin/com/kyant/backdrop/`.

## Dùng lại component

```html
<script type="module" src="./src/flight-taskbar.js"></script>
<flight-taskbar selected-index="0" theme="light" style="width: 360px"></flight-taskbar>
```

```js
const taskbar = document.querySelector('flight-taskbar');
taskbar.addEventListener('tabchange', ({ detail }) => {
  console.log(detail.index, detail.label); // index từ 0 đến 4
  // Đổi màn hình/nội dung ứng dụng tại đây.
});
taskbar.selectedIndex = 2; // Đổi bằng API không phát tabchange.
taskbar.setSettings({ blurRadius: 16, surfaceAlpha: 0.3, depthEffect: true });
console.log(taskbar.settings); // Bản sao các thông số hiện tại.
taskbar.resetSettings();       // Khôi phục thông số taskbar gốc.
```

`theme="light" | "dark"`; bỏ thuộc tính để theo hệ thống. `dir="rtl"` đảo hướng tab/kéo.
Tab/Shift+Tab, phím mũi tên, Home/End và Enter/Space đều được hỗ trợ.
Component tôn trọng `prefers-reduced-motion`. Selection của demo được lưu trong sessionStorage.
Mỗi instance có state riêng; component tự dừng animation khi đã ổn định và giải phóng tài nguyên
khi bị gỡ khỏi DOM.

### Nền tuỳ chỉnh

WebGL không đọc trực tiếp nội dung DOM phía sau như Compose `LayerBackdrop`. Mặc định component
capture gradient của demo. Khi tích hợp vào nền khác, cung cấp canvas/image đã tải từ cùng origin
(hoặc có CORS), vẽ theo viewport; CSS nền trang phải dùng cùng hình ảnh đó:

```js
taskbar.backdrop = backgroundCanvas; // CanvasImageSource phủ viewport.
taskbar.refreshBackdrop();          // Gọi sau khi vẽ lại canvas nền.
taskbar.refresh();                  // Gọi nếu layout di chuyển mà không resize/scroll.
```

Blur được xấp xỉ bằng 9 mẫu shader ở bán kính có thể điều chỉnh (mặc định 8px). Raster chữ, antialiasing và shadow của browser
có thể khác Android; đây là bản chuyển công thức và tương tác, không cam kết pixel-identical.
Trình duyệt không có WebGL2 hoặc mất context sẽ dùng CSS blur và cùng logic tương tác;
fallback không có khúc xạ/tách màu. Context khôi phục sẽ tự bật lại shader.

## Kiểm thử

```powershell
npm install
npm run build
npm test
```

Windows dùng Chrome đã cài; môi trường khác cần `npx playwright install chromium`.
Có thể chọn browser qua `PLAYWRIGHT_CHANNEL` hoặc `PLAYWRIGHT_EXECUTABLE_PATH`.
Tests chạy bản build ở port 5180, kiểm tra WebGL, chọn tab, drag ngoài thanh, touch drag,
keyboard/RTL, mobile resize, dark/reduced-motion, persistence, pointer cancellation,
context loss/restore, CSS fallback, nền có hoa văn và nhiều instance/reconnect.
Có 31 bài kiểm thử, gồm kiểm tra khoảng slider theo Kotlin, uniform/blur thực tế,
preset/reset, lưu thông số, spring, điều chỉnh bằng bàn phím và layout bảng trên mobile.
Kiểm thử quang học đo kích thước icon trên pixel GPU khi tăng scale kính, kiểm tra
quang sai thay đổi vùng viền nhưng không thay đổi vùng trong của lens.
Gallery kiểm tra chuyển trang/dispose, native button và disabled, toggle kéo/huỷ/RTL,
slider bàn phím, filter đúng công thức và uniform, Control Center mở/đóng/kéo/mobile,
CSS fallback, nhiều vòng chuyển trang, reconnect/context restore và các đường dẫn ZIP/Kotlin.
Gói ZIP còn được chạy như một trang mẫu độc lập; kiểm tra byte của các bản Kotlin tham chiếu
so với file gốc và thử tương tác mà không nạp bất kỳ gallery/demo JS nào.
Screenshot nằm trong `test-results/`.
Các bài mới đo alpha SDF thật, con trỏ phóng đại1.5×, alpha smoothstep của progressive blur,
virtualization đến item100, matrix bù zoom/xoay, modal/focus, cảm biến opt-in/cleanup,
tween/resample nền không phát event, CSS fallback và mobile cho toàn bộ trang mới.

## Tài liệu nền tảng và giấy phép

- [WebGL2 / GLSL ES 3.00 — Khronos](https://registry.khronos.org/webgl/specs/latest/2.0/)
- [Pointer capture — MDN](https://developer.mozilla.org/en-US/docs/Web/API/Element/setPointerCapture)
- Mã gốc: Copyright 2025 Kyant, Apache-2.0. Giữ lại `LICENSE` và `NOTICE`.
