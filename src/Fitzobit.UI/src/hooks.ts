import { useCallback, useEffect, useRef, useState, type DependencyList } from 'react';
import { api, type DictionaryOptions } from './api';

export interface ApiState<T> {
  data: T | null;
  error: string | null;
  loading: boolean;
  reload: () => void;
}

/**
 * Loads data from the API; re-runs when `deps` change or `reload()` is called, and aborts stale requests.
 * Existing data stays visible while a reload is in flight.
 */
export function useApi<T>(load: (signal: AbortSignal) => Promise<T>, deps: DependencyList = []): ApiState<T> {
  const [data, setData] = useState<T | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [version, setVersion] = useState(0);
  const loadRef = useRef(load);
  loadRef.current = load;

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError(null);
    loadRef
      .current(controller.signal)
      .then((result) => setData(result))
      .catch((e: unknown) => {
        if (controller.signal.aborted) return;
        setError(e instanceof Error ? e.message : 'Request failed');
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    return () => controller.abort();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [version, ...deps]);

  const reload = useCallback(() => setVersion((v) => v + 1), []);
  return { data, error, loading, reload };
}

/** Debounces a fast-changing value (e.g. a search box). */
export function useDebounced<T>(value: T, delayMs = 300): T {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const handle = setTimeout(() => setDebounced(value), delayMs);
    return () => clearTimeout(handle);
  }, [value, delayMs]);
  return debounced;
}

/**
 * Active drop-down values (preference, meal type, frequency, status) from the database. Never hard-code these lists:
 * administrators manage them in Settings → Dropdown values.
 */
export function useDictionaries(): ApiState<DictionaryOptions> {
  return useApi(api.dictionaries);
}
