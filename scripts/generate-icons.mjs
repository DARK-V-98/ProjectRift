// Generates round favicons / app icons for every device from public/logo.png.
//   node scripts/generate-icons.mjs   (or: npm run icons)
//
// Outputs:
//   app/icon.png          512  round, transparent corners  (browser tab / PWA)
//   app/apple-icon.png    180  round on black              (iOS home screen)
//   app/favicon.ico       16/32/48 round                   (legacy favicon)
//   public/apple-touch-icon.png        180 round on black
//   public/icons/icon-192.png          192 round
//   public/icons/icon-512.png          512 round
import sharp from "sharp";
import pngToIco from "png-to-ico";
import { existsSync, mkdirSync, writeFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const SRC = join(root, "public", "logo.png");

if (!existsSync(SRC)) {
  console.error(`\n✗ Source logo not found at: ${SRC}`);
  console.error("  Save your logo (the square PR artwork) there, then re-run.\n");
  process.exit(1);
}

// Circular mask of a given size — transparent outside the inscribed circle.
const circleMask = (size) =>
  Buffer.from(
    `<svg width="${size}" height="${size}"><circle cx="${size / 2}" cy="${
      size / 2
    }" r="${size / 2}" fill="#fff"/></svg>`
  );

// Round PNG buffer. onBlack=true fills corners with the brand black instead
// of transparency (used for iOS tiles so the icon reads as a clean circle).
async function round(size, { onBlack = false } = {}) {
  let img = sharp(SRC).resize(size, size, { fit: "cover" });
  if (onBlack) {
    img = sharp(SRC)
      .resize(size, size, { fit: "cover" })
      .flatten({ background: "#05070D" });
  }
  return img
    .composite([{ input: circleMask(size), blend: "dest-in" }])
    .png()
    .toBuffer();
}

const iconsDir = join(root, "public", "icons");
if (!existsSync(iconsDir)) mkdirSync(iconsDir, { recursive: true });

const write = (p, buf) => {
  writeFileSync(p, buf);
  console.log("  ✓", p.replace(root + "\\", "").replace(root + "/", ""));
};

console.log("\nGenerating round icons from public/logo.png …");

write(join(root, "app", "icon.png"), await round(512));
write(join(root, "app", "apple-icon.png"), await round(180, { onBlack: true }));
write(join(root, "public", "apple-touch-icon.png"), await round(180, { onBlack: true }));
write(join(iconsDir, "icon-192.png"), await round(192));
write(join(iconsDir, "icon-512.png"), await round(512));

// favicon.ico from round 16/32/48 PNGs
const icoBuf = await pngToIco([await round(48), await round(32), await round(16)]);
write(join(root, "app", "favicon.ico"), icoBuf);

console.log("\n✓ Done. Icons ready for all devices.\n");
