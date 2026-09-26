import { Article } from '../models';

export function articleImageSource(article: Article): string | null {
  if (article.imageUrl)
    return article.imageUrl;
  if (article.imageBase64)
    return `data:${article.imageMimeType || 'image/jpeg'};base64,${article.imageBase64}`;
  return null;
}
