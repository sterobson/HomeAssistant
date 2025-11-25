# Mock API Documentation

The frontend can run in **mock mode** without needing a backend. This is useful for:
- Demo deployments (like GitHub Pages)
- Frontend development when the backend is unavailable
- Testing UI changes without affecting real data

## How It Works

When `VITE_USE_MOCK_API=true`, the frontend uses mock data from `src/services/mockData.js` instead of calling the real API.

### Mock Data Includes:
- **4 Rooms**: Kitchen, Games Room, Bedroom 1, Dining Room
- **Realistic schedules** for each room with different temperatures and times
- **Room states** with current temperatures and heating status
- **Temperature simulation** - temperatures change gradually over time to simulate heating on/off

## Using Mock Mode

### For Local Development

Create a `.env` file in the `Frontend` directory:

```bash
VITE_USE_MOCK_API=true
VITE_HOUSE_ID=00000000-0000-0000-0000-000000000000
```

Then run:
```bash
npm run dev
```

### For GitHub Pages

The GitHub Actions workflow is already configured to use mock data. Just push to the `testing` branch:

```bash
git push origin testing
```

### Switching to Real API

When your backend is deployed, update the workflow or environment variables:

1. Set `VITE_USE_MOCK_API=false`
2. Set `VITE_API_URL` to your backend URL
3. Rebuild and deploy

## Features in Mock Mode

✅ **View schedules** - See all rooms and their heating schedules
✅ **Edit schedules** - Change times, temperatures (saves in memory only)
✅ **Add/delete schedules** - Modify the schedule list
✅ **Live temperatures** - Simulated temperature changes every 5 seconds
✅ **Heating status** - Shows which rooms have heating active
✅ **Boost mode** - Can test boost functionality (simulated)

❌ **Persistence** - Changes don't persist (resets on page refresh)
❌ **Real heating control** - No actual heating devices are controlled
❌ **SignalR updates** - Real-time updates from backend not available

## Customizing Mock Data

Edit `src/services/mockData.js` to:
- Add more rooms
- Change temperature ranges
- Modify schedule times
- Adjust simulation behavior

The temperature simulation can be controlled with:
```javascript
import { startTemperatureSimulation, stopTemperatureSimulation } from './mockData.js'

// Start simulation with callback
startTemperatureSimulation((updatedStates) => {
  console.log('Temperature updated:', updatedStates)
})

// Stop simulation
stopTemperatureSimulation()
```
