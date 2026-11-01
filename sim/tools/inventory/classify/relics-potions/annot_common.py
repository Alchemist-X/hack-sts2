A = {}


def P(ptype, call, site, source, min_sel, max_sel, flags=''):
    return {'type': ptype, 'call': call, 'site': site, 'source': source,
            'min': min_sel, 'max': max_sel, 'flags': flags}


def R(name, tier, tier_combat, run_kind, batch, m='', c='', r='', p=None, g='', s='', k='', n='', q='', a=None, v=None):
    if name in A:
        raise ValueError('duplicate annotation ' + name)
    A[name] = dict(tier=tier, tier_combat=tier_combat, run_kind=run_kind, batch=batch,
                   m=m, c=c, r=r, p=p or [], g=g, s=s, k=k, n=n, q=q, a=a, v=v)


B = {}


def Q(name, tier, batch, m='', c='', r='', p=None, g='', s='', k='', n='', q='', a=None, v=None):
    if name in B:
        raise ValueError('duplicate potion annotation ' + name)
    B[name] = dict(tier=tier, batch=batch, m=m, c=c, r=r, p=p or [], g=g, s=s, k=k, n=n, q=q, a=a, v=v)
