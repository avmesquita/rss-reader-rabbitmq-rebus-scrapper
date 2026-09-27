export interface Feed {
  id: string;
  name: string;
  url: string;
  description?: string;
  pollIntervalMinutes: number;
  createdAt: string;
  lastCheckedAt?: string;
  nextScheduledAt?: string;
  lastError?: string;
  lastCollectedCount: number;
}

export interface Article {
  id: number;
  feedId: string;
  feedName: string;
  title: string;
  url: string;
  author?: string;
  excerpt?: string;
  contentHtml?: string;
  contentText?: string;
  imageUrl?: string;
  imageBase64?: string;
  imageMimeType?: string;
  category?: string;
  publishedAt?: string;
  isFavorite: boolean;
  isHidden: boolean;
  isRead: boolean;
  collectedAt: string;
}

export interface ArticlePage {
  items: Article[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
  categories: string[];
}

export interface Dashboard {
  stats: {
    totalArticles: number;
    visibleArticles: number;
    hiddenArticles: number;
    favorites: number;
    readArticles: number;
  };
  feeds: Feed[];
}

export type DashboardTab = 'stats' | 'feeds' | 'telemetry' | 'system';

export interface DebugStatus {
  generatedAt: string;
  api: { status: string; recentErrors: ApiError[] };
  rabbit: { available: boolean; error?: string; queues: RabbitQueue[]; errorQueues: RabbitQueue[] };
}

export interface ApiError {
  occurredAt: string;
  method: string;
  path: string;
  message: string;
}

export interface RabbitQueue {
  name: string;
  messages: number;
  messagesReady: number;
  messagesUnacknowledged: number;
  consumers: number;
}

export interface QueueMessage {
  payload: string;
  payload_bytes: number;
  redelivered: boolean;
  exchange: string;
  routing_key: string;
  message_count: number;
  properties: Record<string, unknown>;
}

export interface PurgeStats {
  purgedArticles: number;
  purgedFeeds: number;
  purgedImages: number;
  purgedErrors: number;
  purgedAt: string;
  before: ArticleCounts;
  after: ArticleCounts;
}

export interface ArticleCounts {
  all: number;
  read: number;
  hidden: number;
  favorites: number;
}

export interface ReadDeletionStats {
  deletedArticles: number;
  before: ArticleCounts;
  after: ArticleCounts;
  deletedAt: string;
}
