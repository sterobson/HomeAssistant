<template>
  <router-link :to="to" class="feature-card">
    <div class="feature-icon">
      <slot name="icon"></slot>
    </div>
    <div class="feature-info">
      <h2>{{ title }}</h2>
      <p>{{ description }}</p>
    </div>
    <svg class="feature-arrow" width="24" height="24" viewBox="0 0 16 16" fill="currentColor">
      <path fill-rule="evenodd" d="M4.646 1.646a.5.5 0 0 1 .708 0l6 6a.5.5 0 0 1 0 .708l-6 6a.5.5 0 0 1-.708-.708L10.293 8 4.646 2.354a.5.5 0 0 1 0-.708z"/>
    </svg>
  </router-link>
</template>

<script setup>
defineProps({
  title: {
    type: String,
    required: true
  },
  description: {
    type: String,
    required: true
  },
  to: {
    type: String,
    required: true
  }
})
</script>

<style scoped>
.feature-card {
  display: flex;
  align-items: center;
  gap: 1rem;
  padding: 1.25rem;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 12px;
  text-decoration: none;
  color: inherit;
  transition: all 0.2s;
  cursor: pointer;
}

.feature-card:hover {
  border-color: var(--border-hover);
  box-shadow: 0 4px 12px var(--shadow);
  transform: translateY(-2px);
}

.feature-card:active {
  transform: translateY(0);
}

.feature-icon {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 56px;
  height: 56px;
  border-radius: 12px;
  background: var(--bg-tertiary);
  color: var(--color-primary);
  flex-shrink: 0;
}

.feature-info {
  flex: 1;
  min-width: 0;
}

.feature-info h2 {
  font-size: 1.1rem;
  font-weight: 600;
  color: var(--text-primary);
  margin: 0 0 0.25rem 0;
}

.feature-info p {
  font-size: 0.9rem;
  color: var(--text-secondary);
  margin: 0;
}

.feature-arrow {
  color: var(--text-tertiary);
  flex-shrink: 0;
  transition: transform 0.2s;
}

.feature-card:hover .feature-arrow {
  transform: translateX(4px);
}

@media (max-width: 600px) {
  .feature-card {
    padding: 1rem;
  }

  .feature-icon {
    width: 48px;
    height: 48px;
  }
}
</style>
