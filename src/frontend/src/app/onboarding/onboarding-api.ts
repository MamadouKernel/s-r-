import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom, Observable } from 'rxjs';
import { API_ROOT, LoginResponse, toApiError } from '../services/api.service';

export interface SignupStarted {
  verificationId: string;
  expiresAt: string;
  resendAvailableAt: string;
}

export interface NewShop {
  email: string;
  commerceName: string;
  activity: string;
  ownerName: string;
  termsVersion: string;
}

/** Session du propriétaire, après l'inscription comme après la connexion. */
export interface OwnerSession extends LoginResponse {
  tenantId: string;
  commerceName: string;
  trialEndsAt: string | null;
}

export interface DeviceEnrolled {
  deviceId: string;
  deviceSecret: string;
  tenantId: string;
  commerceName: string;
  deviceName: string;
}

/** Inscription, connexion du propriétaire, mot de passe oublié et enrôlement d'un appareil : les appels sans jeton d'accès. */
@Injectable({ providedIn: 'root' })
export class OnboardingApi {
  private readonly http = inject(HttpClient);

  startSignup(shop: NewShop): Promise<SignupStarted> {
    return this.call(this.http.post<SignupStarted>(`${API_ROOT}/signup/start`, shop));
  }

  resendCode(verificationId: string): Promise<SignupStarted> {
    return this.call(this.http.post<SignupStarted>(`${API_ROOT}/signup/resend`, { verificationId }));
  }

  completeSignup(verificationId: string, code: string, password: string): Promise<OwnerSession> {
    return this.call(this.http.post<OwnerSession>(`${API_ROOT}/signup/complete`, { verificationId, code, password }));
  }

  ownerLogin(email: string, password: string): Promise<OwnerSession> {
    return this.call(this.http.post<OwnerSession>(`${API_ROOT}/auth/owner/login`, { email, password }));
  }

  async startPasswordReset(email: string): Promise<void> {
    await this.call(this.http.post(`${API_ROOT}/owner/password-reset/start`, { email }));
  }

  async confirmPasswordReset(email: string, code: string, newPassword: string): Promise<void> {
    await this.call(this.http.post(`${API_ROOT}/owner/password-reset/confirm`, { email, code, newPassword }));
  }

  enrollDevice(token: string, deviceName: string | null): Promise<DeviceEnrolled> {
    return this.call(this.http.post<DeviceEnrolled>(`${API_ROOT}/devices/enroll`, { token, deviceName }));
  }

  private async call<T>(request: Observable<T>): Promise<T> {
    try {
      return await firstValueFrom(request);
    } catch (error) {
      throw toApiError(error);
    }
  }
}
