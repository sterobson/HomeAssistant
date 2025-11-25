// Mock data for heating control system
// Used when VITE_USE_MOCK_API is set to 'true'

export const mockSchedulesData = {
  rooms: [
    {
      id: '11111111-1111-1111-1111-111111111111',
      name: 'Kitchen',
      room: 0, // Kitchen enum value
      schedules: [
        {
          id: '21111111-1111-1111-1111-111111111111',
          time: '06:00',
          temperature: 19,
          rampUpMinutes: 30,
          days: null,
          conditions: 0,
          conditionOperator: 0
        },
        {
          id: '21111111-1111-1111-1111-111111111112',
          time: '06:30',
          temperature: 18.5,
          rampUpMinutes: 30,
          days: null,
          conditions: 0,
          conditionOperator: 0
        },
        {
          id: '21111111-1111-1111-1111-111111111113',
          time: '18:00',
          temperature: 19,
          rampUpMinutes: 30,
          days: null,
          conditions: 0,
          conditionOperator: 0
        },
        {
          id: '21111111-1111-1111-1111-111111111114',
          time: '21:30',
          temperature: 16,
          rampUpMinutes: 30,
          days: null,
          conditions: 0,
          conditionOperator: 0
        }
      ]
    },
    {
      id: '22222222-2222-2222-2222-222222222222',
      name: 'Games Room',
      room: 1, // GamesRoom enum value
      schedules: [
        {
          id: '32222222-2222-2222-2222-222222222221',
          time: '01:00',
          temperature: 15,
          rampUpMinutes: 30,
          days: null,
          conditions: 0,
          conditionOperator: 0
        },
        {
          id: '32222222-2222-2222-2222-222222222222',
          time: '06:00',
          temperature: 20,
          rampUpMinutes: 30,
          days: null,
          conditions: 0,
          conditionOperator: 0
        },
        {
          id: '32222222-2222-2222-2222-222222222223',
          time: '07:00',
          temperature: 22,
          rampUpMinutes: 30,
          days: 64, // Sunday
          conditions: 0,
          conditionOperator: 0
        },
        {
          id: '32222222-2222-2222-2222-222222222224',
          time: '07:00',
          temperature: 18,
          rampUpMinutes: 30,
          days: 62, // NotSunday (Mon-Sat)
          conditions: 0,
          conditionOperator: 0
        },
        {
          id: '32222222-2222-2222-2222-222222222225',
          time: '10:00',
          temperature: 15,
          rampUpMinutes: 30,
          days: null,
          conditions: 0,
          conditionOperator: 0
        },
        {
          id: '32222222-2222-2222-2222-222222222226',
          time: '18:00',
          temperature: 21,
          rampUpMinutes: 30,
          days: null,
          conditions: 0,
          conditionOperator: 0
        },
        {
          id: '32222222-2222-2222-2222-222222222227',
          time: '21:00',
          temperature: 16,
          rampUpMinutes: 30,
          days: null,
          conditions: 0,
          conditionOperator: 0
        }
      ]
    },
    {
      id: '33333333-3333-3333-3333-333333333333',
      name: 'Bedroom 1',
      room: 16, // Bedroom1 enum value
      schedules: [
        {
          id: '43333333-3333-3333-3333-333333333331',
          time: '08:00',
          temperature: 19,
          rampUpMinutes: 30,
          days: 30, // Weekdays (Mon-Fri)
          conditions: 0,
          conditionOperator: 0
        },
        {
          id: '43333333-3333-3333-3333-333333333332',
          time: '08:30',
          temperature: 16,
          rampUpMinutes: 30,
          days: 30, // Weekdays
          conditions: 0,
          conditionOperator: 0
        },
        {
          id: '43333333-3333-3333-3333-333333333333',
          time: '21:30',
          temperature: 19,
          rampUpMinutes: 30,
          days: null,
          conditions: 0,
          conditionOperator: 0
        },
        {
          id: '43333333-3333-3333-3333-333333333334',
          time: '21:31',
          temperature: 14,
          rampUpMinutes: 30,
          days: null,
          conditions: 0,
          conditionOperator: 0
        }
      ]
    },
    {
      id: '44444444-4444-4444-4444-444444444444',
      name: 'Dining Room',
      room: 2, // DiningRoom enum value
      schedules: [
        {
          id: '54444444-4444-4444-4444-444444444441',
          time: '06:00',
          temperature: 17,
          rampUpMinutes: 30,
          days: null,
          conditions: 0,
          conditionOperator: 0
        },
        {
          id: '54444444-4444-4444-4444-444444444442',
          time: '21:00',
          temperature: 14,
          rampUpMinutes: 30,
          days: null,
          conditions: 0,
          conditionOperator: 0
        }
      ]
    }
  ]
}

export const mockRoomStatesData = {
  roomStates: [
    {
      roomId: '11111111-1111-1111-1111-111111111111', // Kitchen
      currentTemperature: 19.2,
      heatingActive: false,
      activeScheduleTrackId: '21111111-1111-1111-1111-111111111113',
      lastUpdated: new Date().toISOString()
    },
    {
      roomId: '22222222-2222-2222-2222-222222222222', // Games Room
      currentTemperature: 20.5,
      heatingActive: true,
      activeScheduleTrackId: '32222222-2222-2222-2222-222222222226',
      lastUpdated: new Date().toISOString()
    },
    {
      roomId: '33333333-3333-3333-3333-333333333333', // Bedroom 1
      currentTemperature: 18.8,
      heatingActive: false,
      activeScheduleTrackId: '43333333-3333-3333-3333-333333333334',
      lastUpdated: new Date().toISOString()
    },
    {
      roomId: '44444444-4444-4444-4444-444444444444', // Dining Room
      currentTemperature: 16.5,
      heatingActive: false,
      activeScheduleTrackId: '54444444-4444-4444-4444-444444444442',
      lastUpdated: new Date().toISOString()
    }
  ]
}

// Helper to simulate temperature changes over time
let temperatureSimulation = null

export function startTemperatureSimulation(callback) {
  if (temperatureSimulation) {
    clearInterval(temperatureSimulation)
  }

  // Simulate temperature changes every 5 seconds
  temperatureSimulation = setInterval(() => {
    mockRoomStatesData.roomStates.forEach(state => {
      // Simulate gradual temperature changes
      if (state.heatingActive) {
        // Heating on - temperature gradually increases
        state.currentTemperature = Math.min(
          state.currentTemperature + 0.1,
          25 // Max temperature
        )
      } else {
        // Heating off - temperature gradually decreases
        state.currentTemperature = Math.max(
          state.currentTemperature - 0.05,
          14 // Min temperature
        )
      }

      // Round to 1 decimal place
      state.currentTemperature = Math.round(state.currentTemperature * 10) / 10
      state.lastUpdated = new Date().toISOString()
    })

    if (callback) {
      callback(mockRoomStatesData)
    }
  }, 5000)
}

export function stopTemperatureSimulation() {
  if (temperatureSimulation) {
    clearInterval(temperatureSimulation)
    temperatureSimulation = null
  }
}
