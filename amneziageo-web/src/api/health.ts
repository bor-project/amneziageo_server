import { useQuery } from '@tanstack/react-query'
import { client } from './client'

export interface Health {
  status: string
  version: string
  time: string
}

export function useHealth() {
  return useQuery({
    queryKey: ['health'],
    queryFn: async () => (await client.get<Health>('/health')).data,
    refetchInterval: 10000,
  })
}
