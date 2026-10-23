// Simple stylised people for the 3D house (owner, 2026-10-22): the household, sat or standing where they usually
// are, built from a few shapes from the owner's description of each (height, build, hair, glasses, beard). Real-world
// sizes in metres, facing +z, origin on the floor at the middle of the seat (sitting) or between the feet (standing).
import * as THREE from 'three'

const SEAT = 0.47

const mat = (colour, extra = {}) => new THREE.MeshStandardMaterial({ color: colour, roughness: 0.8, ...extra })

function limb(from, to, radius, material) {
  const direction = new THREE.Vector3().subVectors(to, from)
  const mesh = new THREE.Mesh(new THREE.CapsuleGeometry(radius, Math.max(0.001, direction.length() - radius * 2), 4, 10), material)
  mesh.position.copy(from).addScaledVector(direction, 0.5)
  mesh.quaternion.setFromUnitVectors(new THREE.Vector3(0, 1, 0), direction.normalize())
  mesh.castShadow = true
  return mesh
}

function sphere(radius, material, x, y, z, extra = {}) {
  const { phiStart = 0, phiLength = Math.PI * 2, thetaStart = 0, thetaLength = Math.PI } = extra
  const mesh = new THREE.Mesh(new THREE.SphereGeometry(radius, 24, 16, phiStart, phiLength, thetaStart, thetaLength), material)
  mesh.position.set(x, y, z)
  mesh.castShadow = true
  return mesh
}

/** Hair on a head of radius r centred at (0, y, z). */
function hair(style, colour, r, y, z) {
  const group = new THREE.Group()
  const material = mat(colour, { roughness: 0.95, side: THREE.DoubleSide })
  if (style === 'curly') {
    // Tight curls piled up on top, the "broccoli" cut.
    let seed = 5
    const random = () => (seed = (seed * 16807) % 2147483647) / 2147483647
    for (let i = 0; i < 46; i++) {
      const theta = random() * Math.PI * 0.5
      const phi = random() * Math.PI * 2
      const lift = 1.05 + random() * 0.25
      group.add(sphere(r * (0.26 + random() * 0.1), material,
        Math.sin(theta) * Math.cos(phi) * r * lift,
        y + Math.cos(theta) * r * lift + r * 0.12,
        z + Math.sin(theta) * Math.sin(phi) * r * lift - r * 0.08))
    }
    return group
  }
  // A cap over the top and back of the head.
  group.add(sphere(r * 1.07, material, 0, y + r * 0.04, z - r * 0.04, { thetaLength: Math.PI * 0.52 }))
  group.add(sphere(r * 1.06, material, 0, y, z - r * 0.05, { phiStart: Math.PI * 0.15, phiLength: Math.PI * 0.7, thetaStart: Math.PI * 0.3, thetaLength: Math.PI * 0.42 }))
  if (style === 'shoulder') {
    // Hanging to the shoulders at the back and sides, open at the face.
    const fall = new THREE.Mesh(new THREE.CylinderGeometry(r * 1.06, r * 1.22, r * 2.0, 24, 1, true, Math.PI * 0.75, Math.PI * 1.5), material)
    fall.position.set(0, y - r * 0.85, z - r * 0.05)
    fall.castShadow = true
    group.add(fall)
  }
  return group
}

function glasses(r, y, z) {
  const group = new THREE.Group()
  const frame = mat('#1b1c1f', { roughness: 0.4, metalness: 0.3 })
  for (const side of [-1, 1]) {
    const lens = new THREE.Mesh(new THREE.TorusGeometry(r * 0.22, r * 0.035, 8, 20), frame)
    lens.position.set(side * r * 0.34, y + r * 0.12, z + r * 0.9)
    group.add(lens)
    const arm = new THREE.Mesh(new THREE.BoxGeometry(r * 0.03, r * 0.03, r * 0.9), frame)
    arm.position.set(side * r * 0.62, y + r * 0.12, z + r * 0.45)
    group.add(arm)
  }
  const bridge = new THREE.Mesh(new THREE.BoxGeometry(r * 0.24, r * 0.03, r * 0.03), frame)
  bridge.position.set(0, y + r * 0.14, z + r * 0.93)
  group.add(bridge)
  return group
}

function beard(colour, r, y, z) {
  return sphere(r * 1.04, mat(colour, { roughness: 1 }), 0, y, z + r * 0.04, {
    phiStart: Math.PI * 0.05, phiLength: Math.PI * 0.9, thetaStart: Math.PI * 0.58, thetaLength: Math.PI * 0.3,
  })
}

/**
 * A person: { height (m), build ('slender'), hair: 'short' | 'shoulder' | 'curly', hairColour, glasses, beard, top,
 * bottom, skin, pose: 'sit' | 'stand' }.
 */
export function buildFigure(person) {
  const group = new THREE.Group()
  const height = person.height ?? 1.72
  const scale = height / 1.72
  const slender = person.build === 'slender' ? 0.88 : 1
  const skin = mat(person.skin ?? '#e9c6a9', { roughness: 0.7 })
  const top = mat(person.top ?? '#4a5d73')
  const bottom = mat(person.bottom ?? '#2f3540')
  const shoes = mat('#2a2a2a')
  const hip = 0.095 * slender * scale
  const legR = 0.055 * slender * scale
  const armR = 0.04 * slender * scale
  const thigh = 0.44 * scale
  const shin = 0.44 * scale
  const sitting = person.pose === 'sit'

  // Legs: straight down when standing; thighs forward and shins down onto the floor when sitting.
  const hipY = sitting ? SEAT + legR : thigh + shin + 0.04
  for (const side of [-1, 1]) {
    const hipAt = new THREE.Vector3(side * hip, hipY, sitting ? -0.05 : 0)
    const knee = sitting ? new THREE.Vector3(side * hip * 1.05, hipY, -0.05 + thigh) : new THREE.Vector3(side * hip, shin + 0.04, 0.01)
    const ankle = sitting ? new THREE.Vector3(side * hip * 1.05, 0.06, -0.05 + thigh + 0.04) : new THREE.Vector3(side * hip, 0.06, 0)
    group.add(limb(hipAt, knee, legR, bottom))
    group.add(limb(knee, ankle, legR * 0.85, bottom))
    const shoe = new THREE.Mesh(new THREE.BoxGeometry(legR * 1.8, 0.06, 0.24 * scale), shoes)
    shoe.position.set(ankle.x, 0.03, ankle.z + 0.05)
    shoe.castShadow = true
    group.add(shoe)
  }

  // Body, shoulders, arms (forward onto the desk or table when sitting, by the sides when standing).
  const bodyBottom = hipY - legR
  const torso = 0.56 * scale
  const shoulderY = bodyBottom + torso
  const body = limb(new THREE.Vector3(0, bodyBottom + 0.14 * scale, sitting ? -0.07 : 0), new THREE.Vector3(0, shoulderY - 0.1 * scale, sitting ? -0.09 : 0), 0.14 * slender * scale, top)
  body.scale.set(1.15, 1, 0.72)
  group.add(body)
  const shoulderW = 0.2 * slender * scale
  for (const side of [-1, 1]) {
    const shoulder = new THREE.Vector3(side * shoulderW, shoulderY - 0.05 * scale, sitting ? -0.09 : 0)
    const elbow = sitting ? new THREE.Vector3(side * shoulderW * 1.05, shoulderY - 0.3 * scale, 0.08) : new THREE.Vector3(side * shoulderW * 1.1, shoulderY - 0.3 * scale, 0)
    const hand = sitting ? new THREE.Vector3(side * shoulderW * 0.7, Math.max(0.78, shoulderY - 0.4 * scale), 0.34) : new THREE.Vector3(side * shoulderW * 1.12, shoulderY - 0.58 * scale, 0.02)
    group.add(limb(shoulder, elbow, armR, top))
    group.add(limb(elbow, hand, armR * 0.85, skin))
  }

  // Neck and head, with hair, glasses and beard as described.
  const headR = 0.105 * scale
  const neckTop = shoulderY + 0.07 * scale
  const headZ = sitting ? -0.06 : 0.01
  group.add(limb(new THREE.Vector3(0, shoulderY - 0.02, headZ - 0.01), new THREE.Vector3(0, neckTop, headZ), 0.045 * scale, skin))
  const headY = neckTop + headR * 0.9
  const head = sphere(headR, skin, 0, headY, headZ)
  head.scale.set(0.9, 1.08, 0.98)
  group.add(head)
  group.add(hair(person.hair ?? 'short', person.hairColour ?? '#2b211b', headR, headY, headZ))
  if (person.glasses) group.add(glasses(headR, headY, headZ))
  if (person.beard) group.add(beard(person.hairColour ?? '#2b211b', headR, headY, headZ))
  return group
}
