window.documentFiles = {
    previewFiles: id => Array.from(document.getElementById(id)?.files || []).map(file => URL.createObjectURL(file)),
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
