// Tailwind v3 (the version cdn.tailwindcss.com served) over the Razor pages of every project: a module can render a panel
// inside a core page (Admin/Users), so its classes must be in the same stylesheet.
const path = require("path");
const src = path.resolve(__dirname, "../../src");
module.exports = {
  content: [path.join(src, "*/Pages/**/*.cshtml")],
  theme: { extend: {} },
  plugins: [],
};
