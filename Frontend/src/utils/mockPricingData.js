// Mock UK Agile Octopus-style half-hourly pricing data (48 slots)
// Prices in p/kWh. Each slot holds a fixed price for 30 minutes.
// Three tiers: low (02:00-04:30), high (16:00-18:30), medium (all other times).

const mockPricingData = [
  // 00:00 - 01:30: Medium
  { timeMinutes: 0,    importPrice: 20.0, exportPrice: 8.0  },
  { timeMinutes: 30,   importPrice: 20.0, exportPrice: 8.0  },
  { timeMinutes: 60,   importPrice: 20.0, exportPrice: 8.0  },
  { timeMinutes: 90,   importPrice: 20.0, exportPrice: 8.0  },

  // 02:00 - 04:30: Low
  { timeMinutes: 120,  importPrice: 10.0, exportPrice: 3.0  },
  { timeMinutes: 150,  importPrice: 10.0, exportPrice: 3.0  },
  { timeMinutes: 180,  importPrice: 10.0, exportPrice: 3.0  },
  { timeMinutes: 210,  importPrice: 10.0, exportPrice: 3.0  },
  { timeMinutes: 240,  importPrice: 10.0, exportPrice: 3.0  },
  { timeMinutes: 270,  importPrice: 10.0, exportPrice: 3.0  },

  // 05:00 - 15:30: Medium
  { timeMinutes: 300,  importPrice: 20.0, exportPrice: 8.0  },
  { timeMinutes: 330,  importPrice: 20.0, exportPrice: 8.0  },
  { timeMinutes: 360,  importPrice: 20.0, exportPrice: 8.0  },
  { timeMinutes: 390,  importPrice: 20.0, exportPrice: 8.0  },
  { timeMinutes: 420,  importPrice: 20.0, exportPrice: 8.0  },
  { timeMinutes: 450,  importPrice: 20.0, exportPrice: 8.0  },
  { timeMinutes: 480,  importPrice: 20.0, exportPrice: 8.0  },
  { timeMinutes: 510,  importPrice: 20.0, exportPrice: 8.0  },
  { timeMinutes: 540,  importPrice: 20.0, exportPrice: 8.0  },
  { timeMinutes: 570,  importPrice: 20.0, exportPrice: 8.0  },
  { timeMinutes: 600,  importPrice: 20.0, exportPrice: 8.0  },
  { timeMinutes: 630,  importPrice: 20.0, exportPrice: 8.0  },
  { timeMinutes: 660,  importPrice: 20.0, exportPrice: 8.0  },
  { timeMinutes: 690,  importPrice: 20.0, exportPrice: 8.0  },
  { timeMinutes: 720,  importPrice: 20.0, exportPrice: 8.0  },
  { timeMinutes: 750,  importPrice: 20.0, exportPrice: 8.0  },
  { timeMinutes: 780,  importPrice: 20.0, exportPrice: 8.0  },
  { timeMinutes: 810,  importPrice: 20.0, exportPrice: 8.0  },
  { timeMinutes: 840,  importPrice: 20.0, exportPrice: 8.0  },
  { timeMinutes: 870,  importPrice: 20.0, exportPrice: 8.0  },
  { timeMinutes: 900,  importPrice: 20.0, exportPrice: 8.0  },
  { timeMinutes: 930,  importPrice: 20.0, exportPrice: 8.0  },

  // 16:00 - 18:30: High
  { timeMinutes: 960,  importPrice: 48.0, exportPrice: 28.0 },
  { timeMinutes: 990,  importPrice: 48.0, exportPrice: 28.0 },
  { timeMinutes: 1020, importPrice: 48.0, exportPrice: 28.0 },
  { timeMinutes: 1050, importPrice: 48.0, exportPrice: 28.0 },
  { timeMinutes: 1080, importPrice: 48.0, exportPrice: 28.0 },
  { timeMinutes: 1110, importPrice: 48.0, exportPrice: 28.0 },

  // 19:00 - 23:30: Medium
  { timeMinutes: 1140, importPrice: 20.0, exportPrice: 8.0  },
  { timeMinutes: 1170, importPrice: 20.0, exportPrice: 8.0  },
  { timeMinutes: 1200, importPrice: 20.0, exportPrice: 8.0  },
  { timeMinutes: 1230, importPrice: 20.0, exportPrice: 8.0  },
  { timeMinutes: 1260, importPrice: 20.0, exportPrice: 8.0  },
  { timeMinutes: 1290, importPrice: 20.0, exportPrice: 8.0  },
  { timeMinutes: 1320, importPrice: 20.0, exportPrice: 8.0  },
  { timeMinutes: 1350, importPrice: 20.0, exportPrice: 8.0  },
  { timeMinutes: 1380, importPrice: 20.0, exportPrice: 8.0  },
  { timeMinutes: 1410, importPrice: 20.0, exportPrice: 8.0  },
]

export default mockPricingData
