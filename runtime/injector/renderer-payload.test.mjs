import assert from "node:assert/strict";
import test from "node:test";
import { prepareRendererPayload } from "./renderer-payload.mjs";
import { createInput } from "./renderer-test-fixture.mjs";

test("prepares a serialized payload and maps frozen Schema v1 task modes", () => {
  const full = prepareRendererPayload(createInput("full"));
  const hidden = prepareRendererPayload(createInput("hidden"));
  const ambient = prepareRendererPayload(createInput("ambient"));

  assert.equal(full.art.taskMode, "banner");
  assert.equal(hidden.art.taskMode, "off");
  assert.equal(ambient.art.taskMode, "ambient");
  assert.equal(full.art.focusXPercent, 50);
  assert.equal(full.palette.accent, "#CC66EE");
});

test("safe area deterministically adjusts image focus", () => {
  const input = createInput("ambient");
  input.theme.art.safeArea = "top";
  input.theme.art.focusX = 0.2;
  input.theme.art.focusY = 0.8;

  const payload = prepareRendererPayload(input);

  assert.equal(payload.art.focusXPercent, 20);
  assert.equal(payload.art.focusYPercent, 0);
});

test("none safe area preserves the explicit focus point", () => {
  const input = createInput("ambient");
  input.theme.art.safeArea = "none";
  input.theme.art.focusX = 0.31;
  input.theme.art.focusY = 0.73;

  const payload = prepareRendererPayload(input);

  assert.equal(payload.art.focusXPercent, 31);
  assert.equal(payload.art.focusYPercent, 73);
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

  const signature = createInput("ambient");
  signature.image.contentType = "image/jpeg";
  assert.throws(
    () => prepareRendererPayload(signature),
    (error) => error.diagnosticCode === "image_signature_mismatch");
});
