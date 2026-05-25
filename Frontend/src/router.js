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
    path: '/heating2',
    name: 'heating2',
    component: () => import('./views/Heating2View.vue')
  },
]

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes
})

export default router
