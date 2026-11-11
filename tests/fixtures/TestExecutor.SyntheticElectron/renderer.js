import * as THREE from "./node_modules/three/build/three.module.js";

const canvas = document.getElementById("view");
const status = document.getElementById("status");
const scene = new THREE.Scene();
const camera = new THREE.PerspectiveCamera(50, 640 / 360, 0.1, 100);
const cube = new THREE.Mesh(new THREE.BoxGeometry(2, 2, 2), new THREE.MeshStandardMaterial({ color: 0x44aa88 }));
cube.name = "cube";
const sphere = new THREE.Mesh(new THREE.SphereGeometry(0.8, 12, 8), new THREE.MeshStandardMaterial({ color: 0x4488cc }));
sphere.name = "sphere";
sphere.position.set(3, 0, 0);
const cone = new THREE.Mesh(new THREE.ConeGeometry(0.7, 1.4, 10), new THREE.MeshStandardMaterial({ color: 0xcc6644 }));
cone.name = "cone";
cone.position.set(-3, 0, 0);
scene.add(new THREE.AmbientLight(0xffffff, 0.5));
const light = new THREE.DirectionalLight(0xffffff, 2);
light.position.set(4, 8, 6);
scene.add(light, cube, sphere, cone);

let azimuth = 0;
function placeCamera() {
  const radius = 9;
  camera.position.set(Math.sin(azimuth) * radius, 4, Math.cos(azimuth) * radius);
  camera.lookAt(0, 0, 0);
}
placeCamera();

let renderer = null;

const paint = () => {
  if (renderer) {
    renderer.render(scene, camera);
    return;
  }
  const context = canvas.getContext("2d");
  context.fillStyle = "#202028";
  context.fillRect(0, 0, canvas.width, canvas.height);
  for (const object of [cone, cube, sphere]) {
    const point = window.__workbenchScene.project(object.name);
    const rect = canvas.getBoundingClientRect();
    context.fillStyle = "#" + object.material.color.getHexString();
    context.fillRect(point.x - rect.left - 18, point.y - rect.top - 18, 36, 36);
  }
};

let dragging = false;
let lastX = 0;
window.addEventListener("mousedown", (event) => {
  const rect = canvas.getBoundingClientRect();
  if (event.clientX < rect.left || event.clientX > rect.right || event.clientY < rect.top || event.clientY > rect.bottom) return;
  dragging = true;
  lastX = event.clientX;
});
window.addEventListener("mousemove", (event) => {
  if (!dragging) return;
  azimuth += (event.clientX - lastX) * 0.01;
  lastX = event.clientX;
  placeCamera();
  paint();
});
window.addEventListener("mouseup", () => { dragging = false; });

function meshInfo(object) {
  const box = new THREE.Box3().setFromObject(object);
  const position = object.geometry.attributes.position;
  const vertices = [];
  const count = Math.min(position.count, 8);
  for (let index = 0; index < count; index += 1) {
    vertices.push({ x: position.getX(index), y: position.getY(index), z: position.getZ(index) });
  }
  return {
    name: object.name,
    min: { x: box.min.x, y: box.min.y, z: box.min.z },
    max: { x: box.max.x, y: box.max.y, z: box.max.z },
    vertices
  };
}

window.__workbenchScene = {
  read() {
    return {
      camera: { x: camera.position.x, y: camera.position.y, z: camera.position.z, fov: camera.fov, azimuth },
      meshes: [cube, sphere, cone].map(meshInfo)
    };
  },
  project(name) {
    const object = scene.getObjectByName(name);
    if (!object) return null;
    const center = new THREE.Box3().setFromObject(object).getCenter(new THREE.Vector3());
    center.project(camera);
    const rect = canvas.getBoundingClientRect();
    return {
      x: rect.left + (center.x + 1) / 2 * rect.width,
      y: rect.top + (1 - center.y) / 2 * rect.height
    };
  }
};

paint();

let swallowed = false;
document.getElementById("start").addEventListener("click", () => {
  if (!swallowed) {
    swallowed = true;
    return;
  }
  status.textContent = "已开始";
});
document.getElementById("popup").addEventListener("click", () => {
  document.querySelector(".ant-modal").hidden = false;
});
