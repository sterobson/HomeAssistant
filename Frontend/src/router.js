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
]

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes
})

export default router
