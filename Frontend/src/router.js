import { createRouter, createWebHashHistory } from 'vue-router'

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
  }
]

const router = createRouter({
  history: createWebHashHistory(),
  routes
})

export default router
