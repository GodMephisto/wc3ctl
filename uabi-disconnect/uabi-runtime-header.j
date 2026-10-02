function UabiReg takes integer unitType, integer abil returns nothing
    local integer n = LoadInteger(udg_UabiTable, unitType, 0) + 1
    call SaveInteger(udg_UabiTable, unitType, 0, n)
    call SaveInteger(udg_UabiTable, unitType, n, abil)
endfunction

function UabiAdd takes unit u returns nothing
    local integer t = GetUnitTypeId(u)
    local integer n = LoadInteger(udg_UabiTable, t, 0)
    local integer i = 1
    local integer a
    loop
        exitwhen i > n
        set a = LoadInteger(udg_UabiTable, t, i)
        if UnitAddAbility(u, a) then
            call UnitMakeAbilityPermanent(u, true, a)
        endif
        set i = i + 1
    endloop
endfunction
