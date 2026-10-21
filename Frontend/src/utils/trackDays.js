// Which days a schedule track runs on. `days` is the Days flags enum the app uses (Mon = 1, Tue = 2 ... Sun = 64,
// 127 = every day); 0 or missing means every day, as in the app (HeatingControlService: Days.Unspecified).

/** The Days flag for a date's weekday, in local time. */
export const dayFlag = (date) => 1 << ((date.getDay() + 6) % 7)

/**
 * Whether a track with these day flags runs on the given date's weekday.
 * @param {number|null|undefined} days
 * @param {Date} date
 */
export const runsOn = (days, date) => !days || (days & dayFlag(date)) !== 0
