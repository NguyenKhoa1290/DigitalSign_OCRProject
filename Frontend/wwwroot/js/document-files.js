window.documentFiles = {
    createUrl: bytes => URL.createObjectURL(new Blob([bytes], { type: "application/pdf" })),
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
