# **********************************************************************
#
# Copyright (c) 2003-2008 ZeroC, Inc. All rights reserved.
#
# This copy of Ice is licensed to you under the terms described in the
# ICE_LICENSE file included in this distribution.
#
# **********************************************************************

import Ice, math, Test, array

def test(b):
    if not b:
        raise RuntimeError('test assertion failed')

def oneways(communicator, p):

    p = Test.MyClassPrx.uncheckedCast(p.ice_oneway())

    #
    # opVoid
    #
    p.opVoid()

    #
    # opByte
    #
    try:
        p.opByte(0xff, 0x0f)
    except Ice.TwowayOnlyException:
        pass
