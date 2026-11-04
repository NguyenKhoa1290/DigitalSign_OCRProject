window.documentFiles = {
    uploadSelections: new Map(),
    captureUpload: (inputId, maxBytes) => {
        const files = Array.from(document.getElementById(inputId)?.files || []);
        if (!files.length) throw new Error("Chưa chọn tệp tải lên.");
        if (files.reduce((sum, file) => sum + file.size, 0) > maxBytes) throw new Error("Tệp vượt quá giới hạn dung lượng tải lên.");
        const key = `${Date.now()}-${Math.random()}`;
        window.documentFiles.uploadSelections.set(key, files);
        return key;
    },
    releaseUpload: key => window.documentFiles.uploadSelections.delete(key),
    upload: (selection, url, token, fieldName, callback) => new Promise((resolve, reject) => {
        const files = window.documentFiles.uploadSelections.get(selection);
        if (!files?.length) { reject(new Error("Không còn tệp đã chọn.")); return; }
        const form = new FormData();
        for (const file of files) form.append(fieldName, file, file.name);
        const xhr = new XMLHttpRequest();
        let last = 0;
        let settled = false, stallTimer, processingTimer;
        const clearTimers = () => { clearTimeout(stallTimer); clearTimeout(processingTimer); };
        const fail = error => {
            if (settled) return;
            settled = true; clearTimers();
            xhr.abort();
            resolve({ status: 0, body: new Uint8Array(), headers: {}, error });
        };
        const watchUpload = () => {
            clearTimeout(stallTimer);
            stallTimer = setTimeout(() => fail("UploadStalled"), 45000);
        };
        xhr.open("POST", url);
        xhr.responseType = "arraybuffer";
        xhr.setRequestHeader("Authorization", `Bearer ${token}`);
        xhr.upload.onprogress = event => {
            if (settled) return;
            watchUpload();
            const now = Date.now();
            if (now - last < 100 && event.loaded !== event.total) return;
            last = now;
            callback.invokeMethodAsync("Report", event.loaded, event.total, event.lengthComputable).catch(() => {});
        };
        xhr.upload.onload = () => {
            if (settled) return;
            clearTimeout(stallTimer);
            processingTimer = setTimeout(() => fail("ProcessingTimeout"), 180000);
            callback.invokeMethodAsync("Uploaded").catch(() => {});
        };
        xhr.onload = () => {
            if (settled) return;
            settled = true; clearTimers();
            const headers = {};
            for (const line of xhr.getAllResponseHeaders().trim().split(/[\r\n]+/)) {
                const colon = line.indexOf(":");
                if (colon > 0) headers[line.slice(0, colon).trim()] = line.slice(colon + 1).trim();
            }
            resolve({ status: xhr.status, body: new Uint8Array(xhr.response || new ArrayBuffer(0)), headers });
        };
        xhr.onerror = () => fail("UploadNetworkError");
        xhr.onabort = () => fail("UploadAborted");
        xhr.ontimeout = () => fail("UploadStalled");
        watchUpload();
        try { xhr.send(form); } catch { fail("UploadNetworkError"); }
    }),
    // Document previews must stay SDR: Ultra HDR JPEG gain maps can toggle the
    // phone display's HDR mode each time an image enters/leaves the viewport.
    // Only the display copy is encoded; uploadSelections and download URLs keep
    // the original File/Blob bytes (including metadata) unchanged.
    createImagePreview: async (source, maxDimension = 2048) => {
        const image = new Image();
        image.decoding = "async";
        const canvas = document.createElement("canvas");
        try {
            image.src = source;
            await image.decode();
            const limit = Math.min(2048, Math.max(1, Number(maxDimension) || 2048));
            const scale = Math.min(1, limit / Math.max(image.naturalWidth, image.naturalHeight));
            canvas.width = Math.max(1, Math.round(image.naturalWidth * scale));
            canvas.height = Math.max(1, Math.round(image.naturalHeight * scale));
            const context = canvas.getContext("2d", { colorSpace: "srgb", colorType: "unorm8" });
            if (!context) throw new Error("Không tạo được ảnh xem trước.");
            context.drawImage(image, 0, 0, canvas.width, canvas.height);
            const blob = await new Promise((resolve, reject) => canvas.toBlob(
                value => value ? resolve(value) : reject(new Error("Không tạo được ảnh xem trước.")),
                "image/png"));
            return URL.createObjectURL(blob);
        } finally {
            image.src = "";
            canvas.width = canvas.height = 0;
        }
    },
    previewFiles: async id => {
        const files = Array.from(document.getElementById(id)?.files || []), urls = [];
        try {
            for (const file of files) {
                const source = URL.createObjectURL(file);
                if (file.type.startsWith("image/") || /\.(png|jpe?g|webp)$/i.test(file.name)) {
                    try { urls.push(await window.documentFiles.createImagePreview(source, 1600)); }
                    finally { URL.revokeObjectURL(source); }
                } else urls.push(source);
            }
            return urls;
        } catch (error) {
            urls.forEach(url => URL.revokeObjectURL(url));
            throw error;
        }
    },
    clearFileInput: id => { const input = document.getElementById(id); if (input?.type === "file") input.value = ""; },
    scrollToElement: id => {
        const element = document.getElementById(id), container = document.querySelector(".page-container");
        if (!element) return;
        if (container?.contains(element)) {
            container.scrollTo({ top: Math.max(0, container.scrollTop + element.getBoundingClientRect().top - container.getBoundingClientRect().top - 12), behavior: "instant" });
        } else element.scrollIntoView({ block: "start", inline: "nearest", behavior: "instant" });
    },
    scrollToTop: () => {
        document.querySelector(".page-container")?.scrollTo({ top: 0, behavior: "instant" });
    },
    pickFile: id => {
        const input = document.getElementById(id);
        if (input?.type === "file") input.click();
    },
    createUrl: (bytes, type = "application/pdf") => URL.createObjectURL(new Blob([bytes], { type })),
    createDownloadUrl: (bytes, type) => URL.createObjectURL(new Blob([bytes], { type })),
    releaseUrl: url => { if (url) URL.revokeObjectURL(url); },
    download: (url, fileName) => {
        const link = document.createElement("a");
        link.href = url;
        link.download = fileName;
        document.body.appendChild(link);
        link.click();
        link.remove();
    }
};
