import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { OtpInput } from './OtpInput'

function Harness({ onValue }: { onValue: (v: string) => void }) {
  const [v, setV] = useState('')
  return <OtpInput value={v} onChange={(x) => { setV(x); onValue(x) }} />
}

describe('OtpInput', () => {
  it('collects typed digits and ignores non-digits', async () => {
    const spy = vi.fn()
    render(<Harness onValue={spy} />)
    await userEvent.type(screen.getByLabelText('Digit 1'), '1a2')
    expect(spy).toHaveBeenLastCalledWith('12')
  })

  it('fills all boxes from a pasted code', async () => {
    const spy = vi.fn()
    render(<Harness onValue={spy} />)
    screen.getByLabelText('Digit 1').focus()
    await userEvent.paste('123 456')
    expect(spy).toHaveBeenLastCalledWith('123456')
  })

  it('offers one-time-code autofill on the first box', () => {
    render(<Harness onValue={() => {}} />)
    expect(screen.getByLabelText('Digit 1').getAttribute('autocomplete')).toBe('one-time-code')
  })
})
