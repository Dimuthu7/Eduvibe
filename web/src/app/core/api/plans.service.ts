import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { AppConfig } from '../config/app-config';

/** The fixed lists the server accepts for a plan. A new feature or limit is added here and on the server. */
export const PLAN_FEATURES = ['whatsapp', 'drive_materials', 'parent_portal', 'advanced_analytics'] as const;
export const PLAN_LIMITS = ['max_classes', 'max_students_per_class', 'whatsapp_per_month'] as const;
export type PlanFeatureKey = (typeof PLAN_FEATURES)[number];
export type PlanLimitKey = (typeof PLAN_LIMITS)[number];

export const PAYMENT_METHODS = ['cash', 'bank_transfer', 'other'] as const;

export interface Plan {
  id: string;
  name: string;
  description: string | null;
  isDefault: boolean;
  isActive: boolean;
  monthlyPrice: number;
  features: Record<string, boolean>;
  /** A cap per limit; null means no cap. */
  limits: Record<string, number | null>;
}

export interface PlanInput {
  name: string;
  description: string | null;
  monthlyPrice: number;
  isActive: boolean;
  features: Record<string, boolean>;
  limits: Record<string, number | null>;
}

export interface Assignment {
  teacherId: string;
  planId: string;
  planName: string;
  endsOn: string | null;
}

export interface TeacherSubscription {
  id: string;
  planId: string;
  planName: string;
  startsOn: string;
  endsOn: string | null;
  status: 'active' | 'cancelled';
  note: string | null;
  inEffect: boolean;
}

export interface PlanPayment {
  id: string;
  subscriptionId: string;
  planName: string;
  amount: number;
  paidOn: string;
  method: string;
  reference: string | null;
}

export interface TeacherPlan {
  teacherId: string;
  teacherName: string;
  effective: { planId: string | null; planName: string; isDefault: boolean; startsOn: string | null; endsOn: string | null };
  subscriptions: TeacherSubscription[];
  payments: PlanPayment[];
}

export interface SubscriptionInput {
  planId: string;
  startsOn: string | null;
  endsOn: string | null;
  note: string | null;
}

export interface PaymentInput {
  amount: number;
  paidOn: string | null;
  method: string;
  reference: string | null;
}

/** A teacher's own plan. availableOn names the cheapest plan with a feature the teacher does not have. */
export interface MyPlan {
  planName: string;
  endsOn: string | null;
  features: { key: string; enabled: boolean; availableOn: string | null }[];
  limits: { key: string; limit: number | null }[];
}

@Injectable({ providedIn: 'root' })
export class PlansService {
  private readonly http = inject(HttpClient);
  private readonly base = `${inject(AppConfig).apiBaseUrl}/api/plans`;

  plans() {
    return this.http.get<Plan[]>(`${this.base}/plans`);
  }

  createPlan(plan: PlanInput) {
    return this.http.post<Plan>(`${this.base}/plans`, plan);
  }

  updatePlan(id: string, plan: PlanInput) {
    return this.http.put<Plan>(`${this.base}/plans/${id}`, plan);
  }

  assignments() {
    return this.http.get<Assignment[]>(`${this.base}/assignments`);
  }

  teacherPlan(teacherId: string) {
    return this.http.get<TeacherPlan>(`${this.base}/teachers/${teacherId}`);
  }

  assign(teacherId: string, input: SubscriptionInput) {
    return this.http.post<TeacherPlan>(`${this.base}/teachers/${teacherId}/subscription`, input);
  }

  recordPayment(teacherId: string, input: PaymentInput) {
    return this.http.post<TeacherPlan>(`${this.base}/teachers/${teacherId}/payments`, input);
  }

  mine() {
    return this.http.get<MyPlan>(`${this.base}/mine`);
  }
}
