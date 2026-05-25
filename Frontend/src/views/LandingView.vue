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
            <path d="M8 16c3.314 0 6-2 6-5.5 0-1.5-.5-4-2.5-6 .25 1.5-1.25 2-1.25 2C11 4 9 .5 6 0c.357 2 .5 4-2 6-1.25 1-2 2.729-2 4.5C2 14 4.686 16 8 16m0-1c-1.657 0-3-1-3-2.75 0-.75.25-2 1.25-3C6.125 10 7 10.5 7 10.5c-.375-1.25.5-3.25 2-3.5-.179 1-.25 2 1 3 .625.5 1 1.364 1 2.25C11 14 9.657 15 8 15"/>
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
            <path d="M0 6a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2v1h.5a.5.5 0 0 1 .5.5v1a.5.5 0 0 1-.5.5H14v1a2 2 0 0 1-2 2H2a2 2 0 0 1-2-2zm2-1a1 1 0 0 0-1 1v4a1 1 0 0 0 1 1h10a1 1 0 0 0 1-1V6a1 1 0 0 0-1-1zm2 2h2v2H4zm4 0h2v2H8z"/>
          </svg>
        </template>
      </FeatureCard>

      <FeatureCard
        title="Heating 2"
        description="Configure rooms, devices, rules, and schedules"
        to="/heating2"
      >
        <template #icon>
          <svg width="28" height="28" viewBox="0 0 16 16" fill="currentColor">
            <path d="M8.707 1.5a1 1 0 0 0-1.414 0L.646 8.146a.5.5 0 0 0 .708.708L2 8.207V13.5A1.5 1.5 0 0 0 3.5 15h9a1.5 1.5 0 0 0 1.5-1.5V8.207l.646.647a.5.5 0 0 0 .708-.708L13 5.793V2.5a.5.5 0 0 0-.5-.5h-1a.5.5 0 0 0-.5.5v1.293zM13 7.207V13.5a.5.5 0 0 1-.5.5h-9a.5.5 0 0 1-.5-.5V7.207l5-5z"/>
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
