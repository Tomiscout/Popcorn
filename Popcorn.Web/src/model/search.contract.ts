export interface MovieDbSearchResult {
  mediaType: MovieDbMediaType;
  id: number;
  title: string;
  description: string;
  releaseDate?: Date;
  posterPath: string;
  backdropPath: string;
  profilePath: string;
  genres: string[];
  ratings: OmdbResult;
}

export interface OmdbResult {
  metascore?: number;
  imdbRating: string;
  rtRating?: number;
  awards: string;
}

export enum MovieDbMediaType {
  Movie = 0,
  TvShow = 1,
  Person = 2,
}

export interface MediaSearchRequest {
  searchText: string;
  isTvShow: boolean;
}

export interface MediaSearchDetailsRequest {
  id: number;
  type: MovieDbMediaType;
}

export interface MediaSearchDetailsResponse {
  movie?: Movie;
  tvShow?: TVShow;
}

export interface TVShow {
  id: number;
  backdropPath: string;
  createdBy: TVShowCreator[];
  episodeRunTime: number[];
  firstAirDate: Date;
  genres: Genre[];
  homepage: string;
  inProduction: boolean;
  languages: string[];
  lastAirDate: Date;
  name: string;
  networks: Network[];
  numberOfEpisodes: number;
  numberOfSeasons: number;
  originCountry: string[];
  originalLanguage: string;
  originalName: string;
  overview: string;
  popularity: number;
  posterPath: string;
  productionCompanies: ProductionCompanyInfo[];
  seasons: Season[];
  keywords: Keyword[];
}

export interface Movie {
  id: number;
  title: string;
  isAdultThemed: boolean;
  backdropPath: string | null;
  movieCollectionInfo: CollectionInfo | null;
  budget: number;
  genres: Genre[];
  homepage: string | null;
  imdbId: string | null;
  originalLanguage: string;
  originalTitle: string;
  overview: string;
  popularity: number;
  posterPath: string | null;
  productionCompanies: ProductionCompanyInfo[];
  productionCountries: Country[];
  releaseDate: string; // "2001-05-18T00:00:00"
  revenue: number;
  runtime: number;
  spokenLanguages: Language[];
  status: string;
  tagline: string;
  isVideo: boolean;
  voteAverage: number;
  voteCount: number;
  keywords: Keyword[];
}

export interface CollectionInfo {
  id: number;
  name: string;
  posterPath: string | null;
  backdropPath: string | null;
}

export interface Genre {
  id: number;
  name: string;
}

export interface ProductionCompanyInfo {
  id: number;
  name: string;
}

export interface Country {
  iso3166Code: string;
  name: string;
}

export interface Language {
  iso639Code: string;
  name: string;
}

export interface Keyword {
  id: number;
  name: string;
}

export interface TVShowCreator {
  id: number;
  name: string;
  profilePath: string;
}

export interface Network {
  id: number;
  name: string;
  logoPath: string;
  originCountry: string;
}

export interface Season {
  id: number;
  name: string;
  airDate: Date;
  episodeCount: number;
  posterPath: string;
}
