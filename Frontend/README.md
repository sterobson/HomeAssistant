# Heating Control Frontend

A mobile-friendly Vue.js application for controlling your house's central heating system.

## Features

- **Room-based Control**: Manage heating schedules for each room independently
- **Collapsible Cards**: Organized UI with collapsible room cards
- **Schedule Management**: Add, edit, and delete heating schedules
- **Flexible Scheduling**: Set time, temperature, and conditions for each schedule
- **Condition Support**: Configure schedules for specific days or conditions (e.g., occupied/away)
- **Mobile Responsive**: Optimized for mobile devices
- **Mock API**: Currently uses mock API calls for development

## Project Structure

```
Frontend/
├── src/
│   ├── components/
│   │   ├── RoomCard.vue          # Collapsible room card component
│   │   ├── ScheduleItem.vue      # Individual schedule display
│   │   └── ScheduleEditor.vue    # Add/edit schedule modal
│   ├── services/
│   │   └── heatingApi.js         # API service (currently mocked)
│   ├── views/
│   │   └── HeatingView.vue       # Main view
│   ├── App.vue                   # Root component
│   └── main.js                   # Application entry point
├── index.html
├── vite.config.js
└── package.json
```

## Getting Started

### Prerequisites

- Node.js (v16 or higher)
- npm or yarn

### Installation

1. Install dependencies:
   ```bash
   npm install
   ```

2. Run the development server:
   ```bash
   npm run dev
   ```

3. Open your browser to `http://localhost:3000`

### Building for Production

```bash
npm run build
```

The built files will be in the `dist` directory.

## API Integration

Currently, the app uses mock API calls defined in `src/services/heatingApi.js`. To integrate with your C# backend:

1. Update `heatingApi.js` to point to your actual API endpoints
2. Replace mock implementations with real HTTP calls (using fetch or axios)
3. Expected API endpoints:
   - `GET /api/schedules` - Returns all room schedules
   - `POST /api/schedules` - Saves updated schedules

### Expected Data Structure

```json
{
  "rooms": [
    {
      "id": 1,
      "name": "Living Room",
      "schedules": [
        {
          "id": 1,
          "time": "06:00",
          "temperature": 20,
          "conditions": "Mon,Tue,Wed,Thu,Fri"
        }
      ]
    }
  ]
}
```

## Schedule Conditions

Conditions can include:
- **Days of week**: Mon, Tue, Wed, Thu, Fri, Sat, Sun
- **Custom conditions**: Occupied, Away, or any custom condition your backend supports
- Multiple conditions are comma-separated

## Development Notes

- Built with Vue 3 Composition API
- Uses Vite for fast development and building
- No external UI library dependencies (pure CSS)
- Mobile-first responsive design
