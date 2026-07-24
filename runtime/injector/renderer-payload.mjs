import { protocolError } from "./security.mjs";

const exactColorPattern = /^#[0-9a-f]{6}(?:[0-9a-f]{2})?$/iu;
const exactUuidPattern =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/iu;
const allowedVariants = new Set(["auto", "light", "dark"]);
const allowedSizes = new Set(["cover", "contain", "crop"]);
const allowedSafeAreas = new Set([
  "auto",
  "center",
  "top",
  "bottom",
  "left",
  "right",
  "none",
]);
const taskModeMap = new Map([
  ["ambient", "ambient"],
  ["full", "banner"],
  ["hidden", "off"],
  ["banner", "banner"],
  ["off", "off"],
]);
const allowedContentTypes = new Set([
  "image/png",
  "image/jpeg",
  "image/webp",
]);
const paletteKeys = [
  "background",
  "panel",
  "accent",
  "text",
  "muted",
  "border",
];
const artKeys = [
  "file",
  "focusX",
  "focusY",
  "safeArea",
  "size",
  "homeOpacity",
  "homeOverlay",
  "taskMode",
  "taskOpacity",
  "taskOverlay",
  "blur",
];

export function prepareRendererPayload(input) {
  assertPlainObject(input, "payload_root");
  assertExactKeys(input, ["theme", "image"], "payload_root");
  const { theme, image } = input;
  assertPlainObject(theme, "theme");
  assertExactKeys(
    theme,
    ["schemaVersion", "id", "name", "variant", "palette", "art"],
    "theme");
  if (theme.schemaVersion !== 1) {
    throw validationError("unsupported_schema");
  }
  if (typeof theme.id !== "string" || !exactUuidPattern.test(theme.id)) {
    throw validationError("invalid_theme_id");
  }
  if (typeof theme.name !== "string" ||
      theme.name.length === 0 ||
      theme.name.length > 120 ||
      [...theme.name].some((value) => /\p{Cc}/u.test(value))) {
    throw validationError("invalid_theme_name");
  }
  if (!allowedVariants.has(theme.variant)) {
    throw validationError("invalid_variant");
  }

  assertPlainObject(theme.palette, "palette");
  assertExactKeys(theme.palette, paletteKeys, "palette");
  const palette = Object.fromEntries(paletteKeys.map((key) => {
    const color = theme.palette[key];
    if (typeof color !== "string" || !exactColorPattern.test(color)) {
      throw validationError(`invalid_color_${key}`);
    }
    return [key, color.toUpperCase()];
  }));

  assertPlainObject(theme.art, "art");
  assertExactKeys(theme.art, artKeys, "art");
  if (typeof theme.art.file !== "string" || theme.art.file.length === 0) {
    throw validationError("invalid_art_file");
  }
  assertRange(theme.art.focusX, 0, 1, "invalid_focus_x");
  assertRange(theme.art.focusY, 0, 1, "invalid_focus_y");
  assertRange(theme.art.homeOpacity, 0, 1, "invalid_home_opacity");
  assertRange(theme.art.homeOverlay, 0, 1, "invalid_home_overlay");
  assertRange(theme.art.taskOpacity, 0, 1, "invalid_task_opacity");
  assertRange(theme.art.taskOverlay, 0, 1, "invalid_task_overlay");
  assertRange(theme.art.blur, 0, 64, "invalid_blur");
  if (!allowedSafeAreas.has(theme.art.safeArea)) {
    throw validationError("invalid_safe_area");
  }
  if (!allowedSizes.has(theme.art.size)) {
    throw validationError("invalid_art_size");
  }
  const rendererTaskMode = taskModeMap.get(theme.art.taskMode);
  if (!rendererTaskMode) {
    throw validationError("invalid_task_mode");
  }

  assertPlainObject(image, "image");
  assertExactKeys(image, ["contentType", "base64"], "image");
  if (!allowedContentTypes.has(image.contentType)) {
    throw validationError("invalid_image_content_type");
  }
  const bytes = decodeBase64(image.base64);
  if (bytes.length === 0 || bytes.length > 16 * 1024 * 1024) {
    throw validationError("invalid_image_size");
  }
  if (!matchesImageSignature(bytes, image.contentType)) {
    throw validationError("image_signature_mismatch");
  }

  const usesCropFocus = theme.art.size === "crop";

  return {
    runtimeVersion: 1,
    themeId: theme.id.toLowerCase(),
    variant: theme.variant,
    palette,
    art: {
      contentType: image.contentType,
      base64: image.base64,
      focusXPercent: (usesCropFocus ? theme.art.focusX : 0.5) * 100,
      focusYPercent: (usesCropFocus ? theme.art.focusY : 0.5) * 100,
      safeArea: theme.art.safeArea,
      size: usesCropFocus ? "cover" : theme.art.size,
      homeOpacity: theme.art.homeOpacity,
      homeOverlay: theme.art.homeOverlay,
      taskMode: rendererTaskMode,
      taskOpacity: theme.art.taskOpacity,
      taskOverlay: theme.art.taskOverlay,
      blur: theme.art.blur,
    },
  };
}

export function readStructuredInput(stream, {
  maxBytes = 24 * 1024 * 1024,
} = {}) {
  return new Promise((resolve, reject) => {
    const chunks = [];
    let bytes = 0;
    stream.on("data", (chunk) => {
      bytes += chunk.length;
      if (bytes > maxBytes) {
        reject(validationError("payload_too_large"));
        stream.pause();
        return;
      }
      chunks.push(chunk);
    });
    stream.on("end", () => {
      try {
        resolve(JSON.parse(Buffer.concat(chunks).toString("utf8")));
      } catch {
        reject(validationError("payload_json_invalid"));
      }
    });
    stream.on("error", () => reject(validationError("payload_read_failed")));
  });
}

function decodeBase64(value) {
  if (typeof value !== "string" ||
      value.length === 0 ||
      value.length % 4 !== 0 ||
      !/^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$/u
        .test(value)) {
    throw validationError("invalid_image_base64");
  }
  return Buffer.from(value, "base64");
}

function matchesImageSignature(bytes, contentType) {
  if (contentType === "image/png") {
    return bytes.length >= 8 &&
      bytes.subarray(0, 8).equals(
        Buffer.from([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]));
  }
  if (contentType === "image/jpeg") {
    return bytes.length >= 4 &&
      bytes[0] === 0xFF &&
      bytes[1] === 0xD8 &&
      bytes.at(-2) === 0xFF &&
      bytes.at(-1) === 0xD9;
  }
  return bytes.length >= 12 &&
    bytes.subarray(0, 4).toString("ascii") === "RIFF" &&
    bytes.subarray(8, 12).toString("ascii") === "WEBP";
}

function assertRange(value, minimum, maximum, code) {
  if (typeof value !== "number" ||
      !Number.isFinite(value) ||
      value < minimum ||
      value > maximum) {
    throw validationError(code);
  }
}

function assertPlainObject(value, code) {
  if (!value || typeof value !== "object" || Array.isArray(value)) {
    throw validationError(`${code}_invalid`);
  }
}

function assertExactKeys(value, expected, code) {
  const actual = Object.keys(value).sort();
  const required = [...expected].sort();
  if (actual.length !== required.length ||
      actual.some((key, index) => key !== required[index])) {
    throw validationError(`${code}_fields_invalid`);
  }
}

function validationError(diagnosticCode) {
  const error = protocolError(diagnosticCode);
  error.code = "validation_failed";
  error.userMessage = "主题渲染 Payload 未通过白名单校验。";
  return error;
}
