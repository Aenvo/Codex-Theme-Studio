import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import {
  MAX_MANAGED_IMAGE_BYTES,
  MAX_STRUCTURED_PAYLOAD_BYTES,
  prepareRendererPayload,
} from "./renderer-payload.mjs";
import { createInput } from "./renderer-test-fixture.mjs";

test("keeps the bundled verification theme aligned with the payload schema", async () => {
  const fixture = JSON.parse(await readFile(
    new URL("./fixtures/verification-theme.json", import.meta.url),
    "utf8"));

  const payload = prepareRendererPayload(fixture);

  assert.equal(payload.themeId, fixture.theme.id);
  assert.equal(payload.art.panelBlur, 0);
  assert.equal(payload.art.cropScale, 1);
});

test("prepares a serialized payload and maps frozen Schema v1 task modes", () => {
  const full = prepareRendererPayload(createInput("full"));
  const hidden = prepareRendererPayload(createInput("hidden"));
  const ambient = prepareRendererPayload(createInput("ambient"));

  assert.equal(full.art.taskMode, "banner");
  assert.equal(hidden.art.taskMode, "off");
  assert.equal(ambient.art.taskMode, "ambient");
  assert.equal(full.art.focusXPercent, 50);
  assert.equal(full.palette.accent, "#CC66EE");
  assert.equal(full.art.panelBlur, 12);
  assert.equal(full.art.cropScale, 1);
});

test("keeps the managed image and Base64 payload limits coordinated", () => {
  assert.equal(MAX_MANAGED_IMAGE_BYTES, 32 * 1024 * 1024);
  assert.equal(MAX_STRUCTURED_PAYLOAD_BYTES, 48 * 1024 * 1024);
  assert.ok(
    MAX_STRUCTURED_PAYLOAD_BYTES >
      Math.ceil(MAX_MANAGED_IMAGE_BYTES / 3) * 4);
});

test("crop mode preserves the explicit focus point and renders as cover", () => {
  const input = createInput("ambient");
  input.theme.art.size = "crop";
  input.theme.art.safeArea = "top";
  input.theme.art.focusX = 0.2;
  input.theme.art.focusY = 0.8;
  input.theme.art.cropScale = 1.6;

  const payload = prepareRendererPayload(input);

  assert.equal(payload.art.focusXPercent, 20);
  assert.equal(payload.art.focusYPercent, 80);
  assert.equal(payload.art.size, "cover");
  assert.equal(payload.art.cropScale, 1.6);
});

test("non-crop image sizes remain centered", () => {
  const input = createInput("ambient");
  input.theme.art.focusX = 0.31;
  input.theme.art.focusY = 0.73;

  const payload = prepareRendererPayload(input);

  assert.equal(payload.art.focusXPercent, 50);
  assert.equal(payload.art.focusYPercent, 50);
  assert.equal(payload.art.size, "cover");
  assert.equal(payload.art.cropScale, 1);
});

test("rejects unknown fields, unsafe colors, bad ranges, and image mismatch", () => {
  const unknown = createInput("ambient");
  unknown.theme.script = "alert(1)";
  assert.throws(
    () => prepareRendererPayload(unknown),
    (error) => error.code === "validation_failed");

  const color = createInput("ambient");
  color.theme.palette.accent = "url(https://example.com)";
  assert.throws(
    () => prepareRendererPayload(color),
    (error) => error.diagnosticCode === "invalid_color_accent");

  const blur = createInput("ambient");
  blur.theme.art.blur = 65;
  assert.throws(
    () => prepareRendererPayload(blur),
    (error) => error.diagnosticCode === "invalid_blur");

  const panelBlur = createInput("ambient");
  panelBlur.theme.art.panelBlur = 65;
  assert.throws(
    () => prepareRendererPayload(panelBlur),
    (error) => error.diagnosticCode === "invalid_panel_blur");

  const cropScale = createInput("ambient");
  cropScale.theme.art.cropScale = 3.1;
  assert.throws(
    () => prepareRendererPayload(cropScale),
    (error) => error.diagnosticCode === "invalid_crop_scale");

  const signature = createInput("ambient");
  signature.image.contentType = "image/jpeg";
  assert.throws(
    () => prepareRendererPayload(signature),
    (error) => error.diagnosticCode === "image_signature_mismatch");
});
