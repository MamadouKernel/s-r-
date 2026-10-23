// « Aujourd'hui » est le jour calendaire local de l'appareil (docs/specs/tableau-de-bord/spec.md). En Côte d'Ivoire,
// le fuseau est UTC sans heure d'été : jour local et jour UTC coïncident.

export interface Range {
  from: string;
  to: string;
}

const ONE_SECOND_MS = 1000;

/** Minuit local du moment donné. Construit par le calendrier, donc juste même un jour de changement d'heure. */
export function startOfLocalDay(moment: Date): Date {
  return new Date(moment.getFullYear(), moment.getMonth(), moment.getDate());
}

/** Du minuit local à maintenant. Le serveur refuse un intervalle vide : à minuit pile, on garde au moins une seconde. */
export function todayRange(now: Date): Range {
  const start = startOfLocalDay(now);
  const end = new Date(Math.max(now.getTime(), start.getTime() + ONE_SECOND_MS));
  return { from: start.toISOString(), to: end.toISOString() };
}

/** Hier, de son minuit jusqu'à la même heure qu'aujourd'hui : la seule comparaison honnête avec une journée en cours. */
export function yesterdaySameTimeRange(now: Date): Range {
  const todayStart = startOfLocalDay(now);
  const yesterdayStart = new Date(todayStart.getFullYear(), todayStart.getMonth(), todayStart.getDate() - 1);
  const elapsed = Math.max(now.getTime() - todayStart.getTime(), ONE_SECOND_MS);
  return { from: yesterdayStart.toISOString(), to: new Date(yesterdayStart.getTime() + elapsed).toISOString() };
}

/** Les `days` derniers jours calendaires locaux, aujourd'hui compris, du minuit local d'il y a `days - 1` jours à maintenant. */
export function lastDaysRange(now: Date, days: number): Range {
  const start = new Date(now.getFullYear(), now.getMonth(), now.getDate() - (Math.max(days, 1) - 1));
  const end = new Date(Math.max(now.getTime(), start.getTime() + ONE_SECOND_MS));
  return { from: start.toISOString(), to: end.toISOString() };
}
