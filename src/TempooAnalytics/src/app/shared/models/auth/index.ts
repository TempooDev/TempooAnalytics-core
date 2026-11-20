export interface LoginRequest { username: string; password: string }
export interface AuthResponse { accessToken: string; expiresIn?: number; user?: any }
export interface ChangePasswordRequest { oldPassword: string; newPassword: string }
export interface SiteSummary {
    siteId: string;
    siteKey: string;
    name: string;
    writeKey?: string;
    totalEvents?: number;
    pageviews?: number;
    lastEventAt?: string;
    allowedOrigins?: string[];
    publicShareToken?: string;
}
