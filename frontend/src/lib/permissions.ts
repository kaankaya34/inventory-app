// Sunucudaki yetki anahtarlarıyla aynı (backend: Security/Permissions.cs).
export const PERM = {
  StockView: 'stock.view',
  StockRestock: 'stock.restock',
  ReservationsManage: 'reservations.manage',
  SimulationRun: 'simulation.run',
  OrdersView: 'orders.view',
  UsersManage: 'users.manage',
} as const

export const ROLE_LABEL: Record<string, string> = {
  CUSTOMER: 'Müşteri',
  STAFF: 'Personel',
  ADMIN: 'Yönetici',
}
