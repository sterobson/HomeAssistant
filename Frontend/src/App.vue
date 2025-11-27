<template>
  <div class="app">
    <header class="app-header">
      <h1>Heating Control</h1>
      <SettingsMenu />
    </header>
    <main class="app-main">
      <HeatingView />
    </main>
  </div>
</template>

<script setup>
import HeatingView from './views/HeatingView.vue'
import SettingsMenu from './components/SettingsMenu.vue'
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
