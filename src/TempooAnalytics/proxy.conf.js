const target =
  process.env["services__gateway__https__0"] ||
  process.env["services__gateway__http__0"] ||
  'http://localhost:8000';

const secure = process.env["NODE_ENV"] !== "development";

module.exports = {
  "/api": {
    target,
    secure,
    changeOrigin: true
  },
};