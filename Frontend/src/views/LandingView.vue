<template>
  <div class="landing-view">
    <div class="feature-grid">
      <FeatureCard
        title="Heating"
        description="Control room temperatures and schedules"
        to="/heating"
      >
        <template #icon>
          <svg width="28" height="28" viewBox="0 0 16 16" fill="currentColor">
            <path d="M5 12.5a1.5 1.5 0 1 1-2-1.415V2.5a.5.5 0 0 1 1 0v8.585A1.5 1.5 0 0 1 5 12.5z"/>
            <path d="M1 2.5a.5.5 0 0 1 .5-.5h1a.5.5 0 0 1 0 1h-1a.5.5 0 0 1-.5-.5zm0 2a.5.5 0 0 1 .5-.5h1a.5.5 0 0 1 0 1h-1a.5.5 0 0 1-.5-.5zm0 2a.5.5 0 0 1 .5-.5h1a.5.5 0 0 1 0 1h-1a.5.5 0 0 1-.5-.5zm0 2a.5.5 0 0 1 .5-.5h1a.5.5 0 0 1 0 1h-1a.5.5 0 0 1-.5-.5z"/>
            <path d="M8.5 2a6.5 6.5 0 1 0 0 13 6.5 6.5 0 0 0 0-13zm0 1a5.5 5.5 0 1 1 0 11 5.5 5.5 0 0 1 0-11z"/>
            <path d="M8.5 4a4.5 4.5 0 1 0 0 9 4.5 4.5 0 0 0 0-9zm0 1a3.5 3.5 0 1 1 0 7 3.5 3.5 0 0 1 0-7z"/>
          </svg>
        </template>
      </FeatureCard>

      <FeatureCard
        title="Battery"
        description="Manage battery charge and discharge schedules"
        to="/battery"
      >
        <template #icon>
          <svg width="28" height="28" viewBox="0 0 16 16" fill="currentColor">
            <path d="M0 6a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2v4a2 2 0 0 1-2 2H2a2 2 0 0 1-2-2V6zm2-1a1 1 0 0 0-1 1v4a1 1 0 0 0 1 1h10a1 1 0 0 0 1-1V6a1 1 0 0 0-1-1H2zm14 3a1.5 1.5 0 0 1-1.5 1.5v-3A1.5 1.5 0 0 1 16 8z"/>
            <path d="M2 6h10v4H2V6z"/>
          </svg>
        </template>
      </FeatureCard>
    </div>
  </div>
</template>

<script setup>
import FeatureCard from '../components/FeatureCard.vue'
</script>

<style scoped>
.landing-view {
  max-width: 600px;
  margin: 0 auto;
  padding: 1rem 0;
}

.feature-grid {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}
</style>
