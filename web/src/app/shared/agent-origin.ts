import { TransactionDto } from '../core/api/dto';

/** 交易是否由 AI client 經由 MCP 記帳（後端 spec D10）；判斷集中在這裡，日後加入其他 client 只改一處。 */
export function recordedByAgent(dto: Pick<TransactionDto, 'createdVia'>): boolean {
  return dto.createdVia?.startsWith('mcp:') ?? false;
}
