import { MutationCache, QueryClient, useMutation, useQueryClient, type QueryKey } from '@tanstack/react-query';
import { ApiError, api, type RequestOptions } from '@/lib/api';

export function createQueryClient(onMutationError?: (error: unknown) => void): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: {
        staleTime: 15_000,
        refetchOnWindowFocus: true,
        retry: (count, error) => !(error instanceof ApiError && error.status >= 400 && error.status < 500) && count < 2,
      },
    },
    mutationCache: new MutationCache({ onError: (error) => onMutationError?.(error) }),
  });
}

interface MutationConfig<TVars> {
  method: NonNullable<RequestOptions['method']>;
  path: (vars: TVars) => string;
  body?: (vars: TVars) => unknown;
  /** Prefixos de chaves a invalidar após o sucesso. */
  invalidate?: QueryKey[];
}

/** Mutação REST com invalidação de cache. O tratamento de erro padrão (toast) fica no MutationCache. */
export function useApiMutation<TVars, TResult = unknown>(config: MutationConfig<TVars>) {
  const queryClient = useQueryClient();
  return useMutation<TResult, unknown, TVars>({
    mutationFn: (vars) => api<TResult>(config.path(vars), { method: config.method, body: config.body ? config.body(vars) : vars }),
    onSuccess: async () => {
      await Promise.all((config.invalidate ?? []).map((queryKey) => queryClient.invalidateQueries({ queryKey })));
    },
  });
}
