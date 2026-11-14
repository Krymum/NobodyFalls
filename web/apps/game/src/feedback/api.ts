import { request } from '../api/client.ts';

/** Ideas, requests and reports (`/api/v1`, GAME_DESIGN §8). Loaded with the screen, not before. */

export type IdeaStatus = 'new' | 'under_review' | 'planned' | 'shipped' | 'declined';
export type IdeaCategory = 'gameplay' | 'social' | 'interface' | 'other';
export type TicketCategory = 'support' | 'appeal' | 'account_deletion';
export type ReportCategory = 'pseudo' | 'harassment' | 'cheating' | 'other';

export const IDEA_STATUSES: readonly IdeaStatus[] = ['new', 'under_review', 'planned', 'shipped', 'declined'];
export const IDEA_CATEGORIES: readonly IdeaCategory[] = ['gameplay', 'social', 'interface', 'other'];
export const TICKET_CATEGORIES: readonly TicketCategory[] = ['support', 'appeal', 'account_deletion'];
export const REPORT_CATEGORIES: readonly ReportCategory[] = ['pseudo', 'harassment', 'cheating', 'other'];

export interface Idea {
  id: number;
  title: string;
  body: string;
  category: IdeaCategory;
  status: IdeaStatus;
  locale: string;
  authorPseudo: string | null;
  mine: boolean;
  hidden: boolean;
  votes: number;
  voted: boolean;
  staffAnswer: string | null;
  answeredAt: string | null;
  mergedInto: number | null;
  createdAt: string;
}

export interface IdeaPage {
  items: Idea[];
  nextOffset: number | null;
}

export interface VoteResult {
  ideaId: number;
  votes: number;
  voted: boolean;
}

export interface TicketSummary {
  id: number;
  category: TicketCategory;
  subject: string;
  status: 'open' | 'waiting_player' | 'resolved' | 'closed';
  updatedAt: string;
  answerWaiting: boolean;
}

export interface Ticket {
  id: number;
  category: TicketCategory;
  subject: string;
  status: TicketSummary['status'];
  createdAt: string;
  messages: { id: number; from: 'player' | 'team'; body: string; at: string }[];
}

export const feedbackApi = {
  ideas: (sort: 'votes' | 'new', status: IdeaStatus | null, offset: number) =>
    request<IdeaPage>(
      'GET',
      `/api/v1/ideas?sort=${sort}&offset=${offset}${status ? `&status=${status}` : ''}`,
    ),
  propose: (body: { title: string; body: string; category: IdeaCategory; turnstileToken: string }) =>
    request<{ id: number }>('POST', '/api/v1/ideas', body),
  vote: (id: number, vote: boolean) =>
    request<VoteResult>(vote ? 'PUT' : 'DELETE', `/api/v1/ideas/${id}/vote`),
  tickets: () => request<TicketSummary[]>('GET', '/api/v1/tickets'),
  ticket: (id: number) => request<Ticket>('GET', `/api/v1/tickets/${id}`),
  openTicket: (body: { category: TicketCategory; subject: string; message: string }) =>
    request<{ id: number }>('POST', '/api/v1/tickets', body),
  reply: (id: number, message: string) =>
    request<undefined>('POST', `/api/v1/tickets/${id}/messages`, { message }),
  report: (body: { pseudo: string; category: ReportCategory; details: string; turnstileToken: string }) =>
    request<{ id: number }>('POST', '/api/v1/reports', { target: 'player', ...body }),
};
