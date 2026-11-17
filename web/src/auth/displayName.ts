/** Friendly name from an email's local part: "jane.doe_99@x.com" -> "Jane Doe". */
export function displayName(email: string | null | undefined): string {
  const local = (email ?? '').split('@')[0].replace(/\d+$/g, '')
  const words = local.split(/[._\-+\s]+/).filter(Boolean)
  if (words.length === 0) return 'there'
  return words.map((w) => w[0].toUpperCase() + w.slice(1).toLowerCase()).join(' ')
}

export function initials(name: string): string {
  const parts = name.split(' ').filter(Boolean)
  return ((parts[0]?.[0] ?? '?') + (parts.length > 1 ? parts[parts.length - 1][0] : '')).toUpperCase()
}

export function greeting(date = new Date()): string {
  const h = date.getHours()
  return h < 12 ? 'Good morning' : h < 18 ? 'Good afternoon' : 'Good evening'
}
