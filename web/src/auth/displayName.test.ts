import { displayName, greeting, initials } from './displayName'

describe('displayName', () => {
  it('title-cases the email local part and drops trailing digits', () => {
    expect(displayName('ibocbina777@gmail.com')).toBe('Ibocbina')
    expect(displayName('jane.doe_99@x.com')).toBe('Jane Doe')
  })
  it('falls back for empty input', () => {
    expect(displayName(null)).toBe('there')
    expect(displayName('777@x.com')).toBe('there')
  })
})

describe('initials / greeting', () => {
  it('builds initials', () => {
    expect(initials('Jane Doe')).toBe('JD')
    expect(initials('Ibocbina')).toBe('I')
  })
  it('greets by time of day', () => {
    expect(greeting(new Date(2026, 0, 1, 8))).toBe('Good morning')
    expect(greeting(new Date(2026, 0, 1, 14))).toBe('Good afternoon')
    expect(greeting(new Date(2026, 0, 1, 20))).toBe('Good evening')
  })
})
