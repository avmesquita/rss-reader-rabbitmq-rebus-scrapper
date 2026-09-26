export interface Feed {
  id: string;
  name: string;
  url: string;
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

export interface PurgeStats {
  purgedArticles: number;
  purgedFeeds: number;
  purgedImages: number;
  purgedErrors: number;
  purgedAt: string;
}