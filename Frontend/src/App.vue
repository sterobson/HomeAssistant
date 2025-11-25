<template>
  <div class="app">
    <header class="app-header">
      <h1>Heating Control</h1>
      <button class="theme-toggle" @click="toggleTheme" :title="theme === 'light' ? 'Switch to dark mode' : 'Switch to light mode'">
        <svg v-if="theme === 'light'" width="20" height="20" viewBox="0 0 16 16" fill="currentColor">
          <path d="M8 11a3 3 0 110-6 3 3 0 010 6zm0 1a4 4 0 100-8 4 4 0 000 8zM8 0a.5.5 0 01.5.5v2a.5.5 0 01-1 0v-2A.5.5 0 018 0zm0 13a.5.5 0 01.5.5v2a.5.5 0 01-1 0v-2A.5.5 0 018 13zm8-5a.5.5 0 01-.5.5h-2a.5.5 0 010-1h2a.5.5 0 01.5.5zM3 8a.5.5 0 01-.5.5h-2a.5.5 0 010-1h2A.5.5 0 013 8zm10.657-5.657a.5.5 0 010 .707l-1.414 1.415a.5.5 0 11-.707-.708l1.414-1.414a.5.5 0 01.707 0zm-9.193 9.193a.5.5 0 010 .707L3.05 13.657a.5.5 0 01-.707-.707l1.414-1.414a.5.5 0 01.707 0zm9.193 2.121a.5.5 0 01-.707 0l-1.414-1.414a.5.5 0 01.707-.707l1.414 1.414a.5.5 0 010 .707zM4.464 4.465a.5.5 0 01-.707 0L2.343 3.05a.5.5 0 11.707-.707l1.414 1.414a.5.5 0 010 .708z"/>
        </svg>
        <svg v-else width="20" height="20" viewBox="0 0 16 16" fill="currentColor">
          <path d="M6 .278a.768.768 0 01.08.858 7.208 7.208 0 00-.878 3.46c0 4.021 3.278 7.277 7.318 7.277.527 0 1.04-.055 1.533-.16a.787.787 0 01.81.316.733.733 0 01-.031.893A8.349 8.349 0 018.344 16C3.734 16 0 12.286 0 7.71 0 4.266 2.114 1.312 5.124.06A.752.752 0 016 .278z"/>
        </svg>
      </button>
    </header>
    <main class="app-main">
      <HeatingView />
    </main>
  </div>
</template>

<script setup>
import HeatingView from './views/HeatingView.vue'
import { useTheme } from './composables/useTheme.js'

const { theme, toggleTheme } = useTheme()
</script>

<style>
.app {
  min-height: 100vh;
  display: flex;
  flex-direction: column;
}

.app-header {
  background-color: var(--bg-header);
  color: var(--text-header);
  padding: 1rem;
  box-shadow: 0 2px 4px var(--shadow);
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.app-header h1 {
  font-size: 1.5rem;
  font-weight: 600;
}

.theme-toggle {
  background: none;
  border: none;
  color: var(--text-header);
  cursor: pointer;
  padding: 0.5rem;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 6px;
  transition: all 0.2s;
}

.theme-toggle:hover {
  background-color: rgba(255, 255, 255, 0.1);
}

.theme-toggle:active {
  transform: scale(0.95);
}

.app-main {
  flex: 1;
  padding: 1rem;
  max-width: 800px;
  margin: 0 auto;
  width: 100%;
}

@media (max-width: 600px) {
  .app-main {
    padding: 0.5rem;
  }

  .app-header h1 {
    font-size: 1.25rem;
  }
}
</style>
