const fs = require("fs");
const path = require("path");
const { execSync } = require("child_process");

module.exports = (featureName) => {
  if (!featureName) {
    console.error("❌ Debes indicar el nombre de la feature");
    return;
  }

  const base = path.join("src", "app", "features", featureName);

  // Create base folders
  [
    base,
    path.join(base, "pages"),
    path.join(base, "components")
  ].forEach(dir => {
    if (!fs.existsSync(dir)) fs.mkdirSync(dir, { recursive: true });
    console.log("📁 Creado:", dir);
  });

  // Create index.ts
  const indexFile = path.join(base, "index.ts");
  if (!fs.existsSync(indexFile)) {
    fs.writeFileSync(indexFile, `export * from './pages/${featureName}.page';`);
    console.log("📝 Creado:", indexFile);
  }

  // Create the main page using the Angular CLI (with NPX)
  try {
    execSync(
      `npx ng g component features/${featureName}/pages/${featureName} --standalone --type=page`,
      { stdio: "inherit", shell: true }
    );
  } catch (err) {
    console.error("❌ Error ejecutando Angular CLI:", err.message);
  }

  console.log(`✔ Feature '${featureName}' generada con éxito`);
};
