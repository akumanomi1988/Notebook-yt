import { expect, test } from 'vitest';
import { transitions } from './workflow';
test('cancelled productions expose no actions', () => expect(transitions.Cancelled).toEqual([]));
test('draft cannot skip brief preparation', () => expect(transitions.Draft).not.toContain('AwaitingNotebook'));
test('awaiting notebook cannot return to draft', () => expect(transitions.AwaitingNotebook).not.toContain('Draft'));
