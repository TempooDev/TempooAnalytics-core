const { execSync } = require("child_process");

module.exports = (feature, name) => {
  if (!feature || !name) {
    console.error("❌ Uso: tempoo g page <feature> <name>");
    return;
  }

  try {
    execSync(
      `npx ng g component features/${feature}/pages/${name} --standalone --type=page`,
      { stdio: "inherit", shell: true }
    );
  } catch (err) {
    console.error("❌ Error ejecutando Angular CLI:", err.message);
  }

  console.log(`✔ Página '${name}' añadida a feature '${feature}'`);
};
