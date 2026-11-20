const { execSync } = require("child_process");

module.exports = (feature, name) => {
  if (!feature || !name) {
    console.error("❌ Uso: tempoo g component <feature> <name>");
    return;
  }

  try {
    execSync(
      `npx ng g component features/${feature}/components/${name}/${name} --standalone --flat`,
      { stdio: "inherit", shell: true }
    );
  } catch (err) {
    console.error("❌ Error ejecutando Angular CLI:", err.message);
  }

  console.log(`✔ Componente '${name}' añadido a feature '${feature}'`);
};
