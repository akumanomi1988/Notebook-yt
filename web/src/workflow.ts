export const transitions: Record<string, string[]> = {
  Draft: ['BriefReady', 'Blocked', 'Cancelled'],
  BriefReady: ['AwaitingNotebook', 'Blocked', 'Cancelled'],
  AwaitingNotebook: ['Blocked', 'Cancelled'],
  Blocked: ['Draft', 'BriefReady', 'AwaitingNotebook', 'Cancelled'],
  Cancelled: [],
};
