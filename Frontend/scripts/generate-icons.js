import sharp from 'sharp'
import { readFileSync, writeFileSync, mkdirSync } from 'fs'
import { join, dirname } from 'path'
import { fileURLToPath } from 'url'

const __dirname = dirname(fileURLToPath(import.meta.url))
const publicDir = join(__dirname, '..', 'public')

const icons = [
  { name: 'battery', svg: 'favicon-battery.svg', bg: '#f0faf0' },
  { name: 'heating', svg: 'favicon-heating.svg', bg: '#faf0e8' },
  { name: 'home', svg: 'favicon.svg', bg: '#e8f0fa' }
]

const sizes = [180, 192, 512]

async function generate() {
  const outDir = join(publicDir, 'icons')
  mkdirSync(outDir, { recursive: true })

  for (const icon of icons) {
    const svgContent = readFileSync(join(publicDir, icon.svg), 'utf-8')

    for (const size of sizes) {
      const padding = Math.round(size * 0.15)
      const innerSize = size - padding * 2

      // Render SVG at inner size, then composite onto a padded background
      const svgBuffer = await sharp(Buffer.from(svgContent))
        .resize(innerSize, innerSize)
        .png()
        .toBuffer()

      await sharp({
        create: {
          width: size,
          height: size,
          channels: 4,
          background: icon.bg
        }
      })
        .composite([{ input: svgBuffer, left: padding, top: padding }])
        .png()
        .toFile(join(outDir, `${icon.name}-${size}.png`))

      console.log(`Generated ${icon.name}-${size}.png`)
    }
  }
}

generate().catch(err => {
  console.error('Failed to generate icons:', err)
  process.exit(1)
})
