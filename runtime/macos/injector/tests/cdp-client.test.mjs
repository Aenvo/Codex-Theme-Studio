import assert from "node:assert/strict";
import test from "node:test";
import {
  buildMainExpression,
  schemaDocument,
  validateRequest,
  validateWebSocket,
} from "../cdp-client.mjs";
import {
  buildRendererExpression,
  runtimeIdentifiers,
  validateTheme,
} from "../renderer-runtime.mjs";

const theme = {
  schemaVersion: 1,
  themeId: "5be08c24-d21f-4db0-bdb0-d6d4cc779d7b",
  variant: "dark",
  palette: {
    background: "#111111",
    panel: "#181818",
    accent: "#2563EB",
    text: "#F5F5F5",
    muted: "#A3A3A3",
    border: "#303030",
  },
};

test("accepts only the palette projection", () => {
  assert.equal(validateTheme(theme), theme);
  assert.throws(() => validateTheme({ ...theme, css: "body{}" }));
  assert.throws(() => validateTheme({ ...theme, url: "https://fixture.invalid" }));
  assert.throws(() => validateTheme({
    ...theme,
    palette: { ...theme.palette, accent: "red" },
  }));
});

test("request rejects extra fields and themes on cleanup", () => {
  const value = {
    command: "apply",
    host: "127.0.0.1",
    port: 9229,
    processId: 42,
    theme,
  };
  assert.equal(validateRequest(value), value);
  assert.throws(() => validateRequest({ ...value, argv: [] }));
  assert.throws(() => validateRequest({ ...value, host: "0.0.0.0" }));
  assert.throws(() => validateRequest({ ...value, command: "cleanup" }));
});

test("websocket validation requires loopback, UUID, and no query", () => {
  const id = "12345678-1234-1234-1234-123456789abc";
  assert.equal(
    validateWebSocket(`ws://127.0.0.1:9229/${id}`, "127.0.0.1", 9229, id)
      .pathname,
    `/${id}`);
  assert.throws(() =>
    validateWebSocket(`ws://0.0.0.0:9229/${id}`, "127.0.0.1", 9229, id));
  assert.throws(() =>
    validateWebSocket(`ws://127.0.0.1:9229/${id}?secret=1`, "127.0.0.1", 9229, id));
});

test("expressions contain fixed identifiers and no private output keys", () => {
  const renderer = buildRendererExpression("apply", theme);
  const main = buildMainExpression("apply", theme);
  for (const value of Object.values(runtimeIdentifiers)) {
    assert.match(renderer, new RegExp(value.replaceAll(".", "\\."), "u"));
  }
  for (const denied of [
    "commandLine", "environment", "conversation", "credential", "token",
  ]) {
    assert.doesNotMatch(main, new RegExp(`"${denied}"\\s*:`, "iu"));
  }
});

test("renderer expressions are syntactically valid", () => {
  for (const mode of ["inspect", "apply", "cleanup"]) {
    const expression = buildRendererExpression(
      mode,
      mode === "apply" ? theme : null);
    assert.doesNotThrow(() => new Function(`return ${expression};`));
  }
});

test("schema is finite and declares only fixed commands", () => {
  assert.deepEqual(
    schemaDocument().commands,
    ["inspect", "apply", "apply-cleanup", "cleanup"]);
  assert.equal(schemaDocument().maximumInputBytes, 64 * 1024);
  assert.equal(schemaDocument().maximumResponseBytes, 128 * 1024);
});
