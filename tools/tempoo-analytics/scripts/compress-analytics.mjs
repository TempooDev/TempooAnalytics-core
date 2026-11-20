import { promises as fs } from "fs";
import { gzip, brotliCompress, constants } from "zlib";
import { promisify } from "util";
import path from "path";

const gzipAsync = promisify(gzip);
const brotliAsync = promisify(brotliCompress);

async function compressFile(inputPath) {
  const source = await fs.readFile(inputPath);

  const gz = await gzipAsync(source, { level: 9 });
  await fs.writeFile(inputPath + ".gz", gz);

  const br = await brotliAsync(source, {
    params: {
      [constants.BROTLI_PARAM_QUALITY]: 11
    }
  });
  await fs.writeFile(inputPath + ".br", br);

  const kb = x => (x / 1024).toFixed(1) + "KB";
  console.log(
    `Compressed ${path.basename(inputPath)} → original ${kb(source.length)}, gzip ${kb(
      gz.length
    )}, br ${kb(br.length)}`
  );
}

async function main() {
  const input = process.argv[2];
  if (!input) {
    console.error("Usage: node compress-analytics.mjs <path/to/file.js>");
    process.exit(1);
  }
  await compressFile(input);
}

main().catch(err => {
  console.error(err);
  process.exit(1);
});
