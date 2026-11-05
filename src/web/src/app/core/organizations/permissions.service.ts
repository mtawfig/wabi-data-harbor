import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { EffectivePermissions, ObjectGrant, RoleMatrix, RowMapping, SetObjectGrantRequest, SetRowMappingRequest } from './permissions.models';

/** Table/column/row permissions of the organization's roles and effective-permission views (specification §10). */
@Injectable({ providedIn: 'root' })
export class PermissionsService {
  private readonly http = inject(HttpClient);

  matrix(roleId: string, applicationId: string): Observable<RoleMatrix> {
    return this.http.get<RoleMatrix>(`/api/permissions/roles/${roleId}/applications/${applicationId}`);
  }

  setGrant(roleId: string, objectId: string, request: SetObjectGrantRequest): Observable<ObjectGrant> {
    return this.http.put<ObjectGrant>(`/api/permissions/roles/${roleId}/objects/${objectId}`, request);
  }

  mapping(objectId: string): Observable<RowMapping> {
    return this.http.get<RowMapping>(`/api/permissions/objects/${objectId}/row-mapping`);
  }

  setMapping(objectId: string, request: SetRowMappingRequest): Observable<RowMapping> {
    return this.http.put<RowMapping>(`/api/permissions/objects/${objectId}/row-mapping`, request);
  }

  mine(): Observable<EffectivePermissions> {
    return this.http.get<EffectivePermissions>('/api/permissions/me');
  }

  effective(userId: string): Observable<EffectivePermissions> {
    return this.http.get<EffectivePermissions>(`/api/permissions/users/${userId}/effective`);
  }
}
