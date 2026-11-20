#!/usr/bin/env node
const [, , cmd, ...args] = process.argv;

switch (cmd) {
  case "g":
  case "generate":
    handleGenerate(args);
    break;

  default:
    console.log("❌ Comando no reconocido");
    console.log("Uso:");
    console.log("  tempoo g feature <name>");
    console.log("  tempoo g page <feature> <name>");
    console.log("  tempoo g component <feature> <name>");
}

function handleGenerate(args) {
  const [type, ...params] = args;

  switch (type) {
    case "feature":
      require("./generators/feature")(params[0]);
      break;

    case "page":
      require("./generators/page")(params[0], params[1]);
      break;

    case "component":
      require("./generators/component")(params[0], params[1]);
      break;

    default:
      console.log("❌ Tipo no reconocido");
  }
}
