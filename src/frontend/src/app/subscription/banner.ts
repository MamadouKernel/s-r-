import { SubscriptionView } from './subscription-api';

export interface BannerAction {
  label: string;
  link: string;
}

export interface Banner {
  tone: 'info' | 'warn' | 'error';
  text: string;
  action: BannerAction | null;
  /** Un second geste, à côté du premier : récupérer ses données quand on est en lecture seule. */
  secondaryAction?: BannerAction | null;
}

const PAGE = '/gestion/abonnement';
/** Le bandeau n'apparaît que dans la dernière semaine : un abonnement en règle ne dit rien. */
const WARN_DAYS = 7;

function shortDate(iso: string): string {
  const date = new Date(iso);
  return `${String(date.getDate()).padStart(2, '0')}/${String(date.getMonth() + 1).padStart(2, '0')}`;
}

function days(count: number): string {
  return `${count} jour${count > 1 ? 's' : ''}`;
}

function sameDay(iso: string, now: Date): boolean {
  const date = new Date(iso);
  return date.getFullYear() === now.getFullYear() && date.getMonth() === now.getMonth() && date.getDate() === now.getDate();
}

/**
 * Le bandeau d'abonnement (docs/specs/abonnement ABO-09) : rien tant que tout va bien, puis un message adapté au profil. Le propriétaire et le
 * gérant voient la date et le moyen d'agir ; les autres profils un message court, sans prix : ils n'ont rien à payer.
 */
export function bannerFor(view: SubscriptionView | null, role: string | undefined, now: Date = new Date()): Banner | null {
  if (!view) {
    return null;
  }
  const manager = role === 'Owner' || role === 'Manager';
  const owner = role === 'Owner';

  if (view.status === 'ReadOnly') {
    if (!manager) {
      return { tone: 'error', text: 'La boutique est en lecture seule. Les ventes en cours sont enregistrées ; prévenez votre responsable.', action: null };
    }
    return owner
      ? {
          tone: 'error',
          text: 'Lecture seule : vos données sont conservées, vous pouvez les consulter. Payez pour reprendre la vente et la gestion.',
          action: { label: 'Payer pour reprendre', link: PAGE },
          secondaryAction: { label: 'Récupérer mes données', link: '/gestion/export' },
        }
      : { tone: 'error', text: 'Lecture seule : les données sont conservées. Prévenez le propriétaire pour reprendre la vente et la gestion.', action: { label: 'Voir l’abonnement', link: PAGE } };
  }

  if (view.status === 'Grace') {
    if (!manager) {
      return { tone: 'warn', text: 'L’abonnement de la boutique est échu. Prévenez votre responsable.', action: null };
    }
    return {
      tone: 'warn',
      text: `Abonnement échu. Plus que ${days(view.daysLeft)} avant la lecture seule (le ${shortDate(view.readOnlyFrom)}).`,
      action: { label: owner ? 'Payer maintenant' : 'Voir l’abonnement', link: PAGE },
    };
  }

  if (!manager || view.daysLeft > WARN_DAYS) {
    return null;
  }
  const subject = view.status === 'Trial' ? 'Votre essai gratuit' : 'Votre abonnement';
  const verb = view.status === 'Trial' ? 'finit' : 'se termine';
  const action = { label: view.daysLeft <= 1 ? 'Payer maintenant' : 'Voir mon abonnement', link: PAGE };
  if (view.daysLeft <= 1) {
    return {
      tone: 'warn',
      text: sameDay(view.endsAt, now)
        ? `${subject} ${verb} aujourd’hui. Payez pour ne rien interrompre.`
        : `${subject} ${verb} demain (le ${shortDate(view.endsAt)}).`,
      action,
    };
  }
  return { tone: view.daysLeft > 3 ? 'info' : 'warn', text: `${subject} ${verb} dans ${days(view.daysLeft)} (le ${shortDate(view.endsAt)}).`, action };
}
