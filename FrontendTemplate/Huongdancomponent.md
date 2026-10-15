# Hướng dẫn AI sử dụng bộ component Liquid Glass

Tài liệu này hướng dẫn tích hợp lại các component trong `FlightTaskbarWeb` vào ứng dụng web mới. Mục tiêu là tái sử dụng giao diện, hiệu ứng và tương tác có sẵn, không dựng lại bằng CSS thuần.

## Điều kiện chạy

- Chạy qua HTTP, không mở trang bằng `file://`.
- Dùng ES modules và Web Components có sẵn; không cần React, Vue hoặc thư viện UI khác.
- Nạp Inter cục bộ trước CSS ứng dụng:

```html
<link rel="stylesheet" href="./src/fonts.css" />
```

```css
body { font-family: Inter, ui-sans-serif, system-ui, sans-serif; }
```

- Giữ `LICENSE`, `NOTICE` và `INTER-OFL.txt` khi phân phối resource kit.

## Chọn component

| Nhu cầu | Component |
| --- | --- |
| Thanh taskbar máy bay 5 ô | `<flight-taskbar>` |
| Nút kính | `<liquid-button>` |
| Công tắc | `<liquid-toggle>` |
| Thanh kéo | `<liquid-slider>` |
| Bảng điều khiển nhanh | `<liquid-control-center>` |
| Hộp thoại / khoá màn hình | `<liquid-dialog>`, `<liquid-lock-screen>` |
| Kính lúp, blur, scroll kính | `<liquid-magnifier>`, `<progressive-blur>`, `<glass-scroll>` |
| Chỉnh vật liệu trực quan | `<glass-playground>` |
| Surface kính tùy chỉnh | `<glass-surface>`, `<adaptive-glass>` |

Nạp API từ một nơi:

```js
import './src/flight-taskbar.js';
import {
  GlassSurface, LiquidButton, LiquidToggle, LiquidSlider,
  LiquidControlCenter, LiquidDialog, LiquidLockScreen,
  LiquidMagnifier, ProgressiveBlur, GlassScroll, GlassPlayground,
  setGlassBackdrop,
} from './src/components/index.js';
```

Các import sẽ đăng ký custom element; sau đó dùng bằng HTML hoặc `document.createElement()`.

## Backdrop: yêu cầu cốt lõi của hiệu ứng kính

Refraction lấy mẫu từ `HTMLCanvasElement` hoặc `HTMLImageElement`. DOM phía sau không tự được WebGL chụp lại. Hãy vẽ nền vào canvas và dùng chính canvas đó làm nền hiển thị.

```js
import { setGlassBackdrop } from './src/components/index.js';

const backdrop = document.querySelector('#glass-backdrop');
const context = backdrop.getContext('2d');

function redrawBackdrop() {
  backdrop.width = innerWidth;
  backdrop.height = innerHeight;
  const gradient = context.createLinearGradient(0, 0, backdrop.width, backdrop.height);
  gradient.addColorStop(0, '#99d6ff');
  gradient.addColorStop(1, '#f9c58b');
  context.fillStyle = gradient;
  context.fillRect(0, 0, backdrop.width, backdrop.height);
  setGlassBackdrop(backdrop);
}

redrawBackdrop();
addEventListener('resize', redrawBackdrop);
```

```css
#glass-backdrop {
  position: fixed; inset: 0; z-index: -1; width: 100%; height: 100%;
}
```

Sau khi đổi pixel canvas, gọi lại `setGlassBackdrop(backdrop)`. Nếu một component có backdrop riêng, gán `element.backdrop = backdrop` rồi `element.refresh(true)`. Ảnh nguồn phải cùng origin hoặc có CORS phù hợp để canvas không bị taint.

## Tích hợp taskbar 5 ô

```html
<canvas id="glass-backdrop"></canvas>
<flight-taskbar id="taskbar"></flight-taskbar>
```

```js
const taskbar = document.querySelector('#taskbar');
taskbar.backdrop = document.querySelector('#glass-backdrop');
taskbar.selectedIndex = 0;

taskbar.addEventListener('tabchange', ({ detail }) => {
  // detail = { index, label }
  showPage(detail.index);
});

function redrawScene() {
  // Cập nhật pixel canvas trước.
  taskbar.refreshBackdrop();
  taskbar.refresh();
}
```

Giữ năm mục nếu cần taskbar máy bay đúng resource gốc. Không phóng to icon quá mạnh: thiết kế hiện tại ưu tiên phản hồi nhấn nhỏ, viền quang sai và refraction. Sau resize, đổi backdrop hoặc hiện lại taskbar, gọi `refresh()`.

## Control, sự kiện và dữ liệu

```html
<liquid-button id="save" label="Lưu" tint="#0a84ff"></liquid-button>
<liquid-toggle id="wifi" label="Wi‑Fi" checked></liquid-toggle>
<liquid-slider id="volume" label="Âm lượng" min="0" max="100" value="65"></liquid-slider>
<liquid-control-center id="control-center"></liquid-control-center>
```

```js
const controlCenter = document.querySelector('#control-center');
save.addEventListener('activate', () => saveSettings());
wifi.addEventListener('change', ({ detail }) => updateWifi(detail.checked));
volume.addEventListener('input', ({ detail }) => setVolume(detail.value));
controlCenter.addEventListener('controlchange', ({ detail }) => applyControlCenterState(detail));
```

`<liquid-toggle>` trên web dùng track `56 × 28 px`, thumb `36 × 24 px`; host giữ vùng chạm cao `44 px`. Đây là điều chỉnh có chủ ý, khác kích thước toggle Kotlin gốc. Control vẫn dùng phần tử native bên trong để hỗ trợ bàn phím và screen reader.

## Xuất cấu hình để đưa cho AI

Trong trang taskbar, nút **Tải config về** xuất `flight-taskbar-config.json`. File có `format`, `version`, `taskbar.settings`, `taskbar.selectedIndex` và `preview.background`; không có dữ liệu cá nhân. Đưa toàn bộ JSON vào prompt và yêu cầu AI áp dụng `taskbar.setSettings(config.taskbar.settings)` cùng tab/nền đã xuất.

## Surface kính tùy chỉnh

```js
const surface = document.createElement('glass-surface');
surface.backdrop = backdrop;
surface.configure({
  radius: 24, blur: 0.18, refractionHeight: 18, refractionAmount: 0.16,
  depth: 0.58, chromatic: 0.04, brightness: 1.04, contrast: 1.03,
  saturation: 1.08, highlight: 0.32, tint: 'rgba(255,255,255,.12)',
});
surface.append(content);
host.append(surface);
```

Dùng `<adaptive-glass>` nếu filter phải tự điều chỉnh theo độ sáng backdrop. Có thể lắng nghe `filterchange` để cập nhật phần UI liên quan. Không dùng giá trị adaptive làm nguồn chân lý cho nghiệp vụ.

## Kiến trúc trang

```text
App shell
├─ Canvas/Image backdrop (nguồn pixel dùng chung)
├─ Nội dung trang ngữ nghĩa: heading, form, list, button
├─ Glass surface / control overlays
└─ flight-taskbar cố định phía dưới
```

Nội dung nghiệp vụ là HTML semantic bình thường; chỉ vỏ và overlay dùng kính. Đừng tạo một canvas/WebGL context cho từng hàng trong danh sách dài. Khi đổi trang, tháo các component không dùng để giải phóng renderer. Tránh lồng nhiều `transform`, `filter` hoặc `overflow: hidden` quanh surface trừ khi đã kiểm thử mẫu backdrop.

## Fallback và accessibility

- Luôn có `label`, text hoặc `aria-label` cho control chỉ có icon.
- Tôn trọng `prefers-reduced-motion`; không thêm animation vô hạn vào toàn bộ surface.
- Không đưa dữ liệu chưa tin cậy vào `innerHTML`.
- Kiểm tra `data-renderer` nếu cần phát hiện fallback; thiếu WebGL không được làm hỏng điều hướng, form hoặc lưu dữ liệu.

## Nguồn đối chiếu

- `src/components/`: Web Component và renderer.
- `src/flight-taskbar.js`: taskbar máy bay.
- `src/fonts.css`, `src/assets/*.woff2`: Inter cục bộ.
- `original-kotlin/`: snapshot Kotlin gốc để đối chiếu hành vi/material.
- `tests/`: kiểm thử tương tác và resource kit.
- `README.md`: API tóm tắt, lệnh chạy và danh sách component.

Khi cần độ giống cao, đọc snapshot Kotlin tương ứng trước rồi sửa component web mục tiêu. Không sửa `original-kotlin/`; đây là tài liệu tham chiếu.

## Checklist bàn giao

1. Chạy `npm run build`.
2. Chạy `npm test` hoặc test của component đã sửa.
3. Kiểm tra Chrome desktop và viewport mobile: touch, Tab/Enter/Space, resize, reduced motion.
4. Dùng backdrop thật và xác nhận khúc xạ lấy đúng vị trí.
5. Kiểm tra CORS của canvas/ảnh backdrop.
6. Giữ attribution Inter cùng license khi xuất resource kit.

## Prompt mẫu cho AI

```text
Dùng FlightTaskbarWeb làm nguồn UI. Giữ Web Components hiện có, nạp src/fonts.css
để dùng Inter cục bộ. Tạo canvas backdrop dùng chung, vẽ nền vào canvas rồi gọi
setGlassBackdrop(canvas). Đặt flight-taskbar 5 ô ở đáy, lắng nghe tabchange để đổi
nội dung trang. Dùng liquid-button, liquid-toggle và liquid-slider cho control; giữ
HTML semantic, keyboard accessibility và fallback khi WebGL không khả dụng. Không
chỉnh original-kotlin; chỉ dùng các file đó để đối chiếu hành vi.
```
