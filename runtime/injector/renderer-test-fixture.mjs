const pngBase64 =
  "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";

export function createInput(taskMode = "ambient") {
  return {
    theme: {
      schemaVersion: 1,
      id: "1296cb77-2297-4992-af72-5c3cc40b32be",
      name: "VM test theme",
      variant: "dark",
      palette: {
        background: "#121018",
        panel: "#201A28E6",
        accent: "#cc66ee",
        text: "#F7F1FA",
        muted: "#B8AEBE",
        border: "#45394E",
      },
      art: {
        file: "background.png",
        focusX: 0.5,
        focusY: 0.5,
        safeArea: "auto",
        size: "cover",
        homeOpacity: 0.82,
        homeOverlay: 0.25,
        taskMode,
        taskOpacity: 0.32,
        taskOverlay: 0.68,
        blur: 4,
        panelBlur: 12,
        cropScale: 1,
      },
    },
    image: {
      contentType: "image/png",
      base64: pngBase64,
    },
  };
}
