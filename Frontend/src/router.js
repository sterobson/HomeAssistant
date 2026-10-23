import { createRouter, createWebHistory } from 'vue-router'

const routes = [
  {
    path: '/',
    name: 'home',
    component: () => import('./views/LandingView.vue')
  },
  {
    path: '/heating',
    name: 'heating',
    component: () => import('./views/HeatingView.vue')
  },
  {
    path: '/battery',
    name: 'battery',
    component: () => import('./views/BatteryView.vue')
  },
  {
    // The house in 3D with every Home Assistant device in its room: live status, and switches for the house's own
    // plugs and lights.
    path: '/3d',
    name: 'house3d',
    component: () => import('./views/House3DView.vue')
  },
  {
    // The page a heating zone's QR code opens. Reached by guests who know only the token, so it
    // must not depend on the house ID; App.vue shows nothing but this view on it.
    path: '/r/:token',
    name: 'guestRoom',
    component: () => import('./views/GuestRoomView.vue')
  },
  {
    // Power-user / recovery page, not linked from anywhere — accessible only
    // by typing the URL. Used for manually restoring missing daily energy
    // data by distributing a total across hours using another day's shape.
    path: '/manual-import',
    name: 'manualImport',
    component: () => import('./views/ManualImportView.vue')
  },
]

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes
})

export default router
