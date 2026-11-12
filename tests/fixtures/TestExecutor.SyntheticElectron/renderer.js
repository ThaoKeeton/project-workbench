import * as THREE from "three";

// The bundle keeps scene, camera, and renderer inside this closure.
// The executor observes them through window.__THREE_DEVTOOLS__, which three.js
// notifies from its constructors. This file does not publish a test hook.
const canvas = document.getElementById("view");
const status = document.getElementById("status");
const scene = new THREE.Scene();
scene.name = "Scene";
const camera = new THREE.PerspectiveCamera(50, 640 / 360, 0.1, 100);
camera.name = "view-camera";
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
scene.add(light, cube, sphere, cone, camera);

let azimuth = 0;
function placeCamera() {
  const radius = 9;
  camera.position.set(Math.sin(azimuth) * radius, 4, Math.cos(azimuth) * radius);
  camera.lookAt(0, 0, 0);
  camera.updateMatrixWorld(true);
}
placeCamera();

// Paint with the 2D canvas. A WebGL context is not required to observe the scene,
// and a missing GPU on CI can tear down the renderer process.
const raycaster = new THREE.Raycaster();
const pointer = new THREE.Vector2();

function paint() {
  const context = canvas.getContext("2d");
  if (!context) return;
  context.fillStyle = "#202028";
  context.fillRect(0, 0, canvas.width, canvas.height);
  for (const object of [cone, cube, sphere]) {
    const center = object.position.clone();
    center.project(camera);
    const x = (center.x + 1) / 2 * canvas.width;
    const y = (1 - center.y) / 2 * canvas.height;
    context.fillStyle = "#" + object.material.color.getHexString();
    context.fillRect(x - 18, y - 18, 36, 36);
  }
}

function pick(clientX, clientY) {
  const rect = canvas.getBoundingClientRect();
  if (clientX < rect.left || clientX > rect.right || clientY < rect.top || clientY > rect.bottom) return null;
  pointer.x = ((clientX - rect.left) / rect.width) * 2 - 1;
  pointer.y = -((clientY - rect.top) / rect.height) * 2 + 1;
  raycaster.setFromCamera(pointer, camera);
  const hits = raycaster.intersectObjects([cube, sphere, cone], false);
  return hits.length ? hits[0].object : null;
}

let dragging = false;
let lastX = 0;
let picked = null;
window.addEventListener("mousedown", (event) => {
  const rect = canvas.getBoundingClientRect();
  if (event.clientX < rect.left || event.clientX > rect.right || event.clientY < rect.top || event.clientY > rect.bottom) return;
  dragging = true;
  lastX = event.clientX;
  picked = pick(event.clientX, event.clientY);
});
window.addEventListener("mousemove", (event) => {
  if (!dragging) return;
  const delta = event.clientX - lastX;
  lastX = event.clientX;
  if (picked) picked.position.x += delta * 0.01;
  else {
    azimuth += delta * 0.01;
    placeCamera();
  }
  paint();
});
window.addEventListener("mouseup", () => {
  dragging = false;
  picked = null;
});

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
